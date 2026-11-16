namespace Drover.Server.Tests;

/// <summary>
/// A bot's request for a person at its screen, answered over the API: who may take over and hand back, the
/// antiforgery token, a call answered once, and that an approval and a request for a person are not answered alike.
/// </summary>
public sealed class ScreenTakeoverApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private readonly MemorySandboxProvider provider = new();

    private readonly ScriptedModel script = new(
        [new ToolCallOutput("c1", "computer_handover", """{"reason":"Sign in with your passkey."}""")],
        [new TextOutput("Thanks.")],
        [new ToolCallOutput("c2", "computer_handover", """{"reason":"Solve the check that you are a person."}""")],
        [new TextOutput("Understood.")]);

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));
        services.AddSingleton<ISandboxProvider>(provider);
        services.AddSandboxTools();
    }

    #endregion

    #region Who Answers

    [Fact]
    public async Task The_runs_person_answers_an_owner_or_admin_takes_over_and_nobody_else_reaches_the_call()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(owner.Email, owner.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var botId = await HandoverBot(asOwner, owner.SpaceId, owner.UserId);

        // The owner's own conversation: the member cannot reach the call at all.
        var (ownerRun, ownerCall) = await Ask(asOwner, owner.SpaceId, botId);
        var calls = $"{Space(owner.SpaceId)}/tool-calls/{ownerCall}";
        var waiting = await WaitForRun(asOwner, owner.SpaceId, ownerRun, "waiting");

        (await asMember.Post($"{calls}/takeover")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a member cannot see a call in another member's private thread");
        (await asMember.Post($"{calls}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asOwner.PostWithoutToken($"{calls}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a state-changing request needs the antiforgery token");
        (await asOwner.Post($"{Space(owner.SpaceId)}/tool-calls/{Guid.CreateVersion7()}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var approve = await asOwner.Post($"{calls}/decision", new ToolDecisionRequest(true));
        approve.StatusCode.Should().Be(HttpStatusCode.Conflict, "a request for a person is handed back, not approved");
        (await asOwner.Post($"{calls}/takeover")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asOwner.Post($"{calls}/handback", new ToolHandbackRequest(false, "No phone with me."))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asOwner.Post($"{calls}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.Conflict, "a call is answered once");
        (await WaitForRun(asOwner, owner.SpaceId, ownerRun, "completed", "failed")).Status.Should().Be("completed");

        // The member's own conversation: they answer for their run, and the owner cannot reach it.
        var (memberRun, memberCall) = await Ask(asMember, owner.SpaceId, botId);
        var theirs = $"{Space(owner.SpaceId)}/tool-calls/{memberCall}";
        await WaitForRun(asMember, owner.SpaceId, memberRun, "waiting");

        (await asMember.Post($"{theirs}/takeover")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "owners and admins use a bot's screen");
        (await asOwner.Post($"{theirs}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.NotFound, "an owner cannot see a member's private call");
        (await asMember.Post($"{theirs}/handback", new ToolHandbackRequest(true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WaitForRun(asMember, owner.SpaceId, memberRun, "completed", "failed")).Status.Should().Be("completed");

        waiting.StatusReason.Should().Be("awaiting_takeover");
        (await AuditActions(owner.SpaceId)).Should().Equal("sandbox.taken_over", "sandbox.handed_back", "sandbox.handed_back");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    /// <summary>An owner's bot with a private sandbox that has a screen, a model that sees, and the handover tool allowed.</summary>
    private async Task<Guid> HandoverBot(ApiClient client, Guid spaceId, Guid userId)
    {
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var saved = await client.Put(
            $"{Space(spaceId)}/bots/{bot.Id}/definition",
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: true), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        await InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = connection.Id,
                Model = "scripted",
                DisplayName = "Scripted",
                Capabilities = new ModelCapabilities { Vision = true },
                LastSeenAt = Time.GetUtcNow(),
            });
            await db.SaveChangesAsync(Token);
            return true;
        });
        var tool = (await Read<List<BotToolEntry>>(await client.Get($"{Space(spaceId)}/bots/{bot.Id}/tools"))).Single(entry => entry.Name == "computer.handover");
        tool.AlwaysAsks.Should().BeTrue();
        (await client.Put($"{Space(spaceId)}/bots/{bot.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("allow"))).EnsureSuccessStatusCode();
        return bot.Id;
    }

    /// <summary>A new private conversation in which the bot asks for a person, and the call that asks.</summary>
    private static async Task<(Guid RunId, Guid ToolCallId)> Ask(ApiClient client, Guid spaceId, Guid botId)
    {
        var thread = await Read<ThreadSummary>(await client.Post($"{Space(spaceId)}/bots/{botId}/threads", new CreateThreadRequest(null)));
        var sent = await Read<SendMessageResponse>(await client.Post($"{Space(spaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("Read my mail")));
        await WaitForRun(client, spaceId, sent.Run.Id, "waiting");
        var call = (await Read<List<MessageResponse>>(await client.Get($"{Space(spaceId)}/threads/{thread.Id}/messages")))
            .SelectMany(message => message.Parts).Single(part => part.Kind == "tool_call");
        return (sent.Run.Id, call.ToolCallId!.Value);
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId, params string[] statuses)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await Read<RunSummary>(await client.Get($"{Space(spaceId)}/runs/{runId}"));

            if (statuses.Contains(run.Status))
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should reach {string.Join(" or ", statuses)}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await tenant.Commit(Token);
        return result;
    }

    private async Task<List<string>> AuditActions(Guid spaceId)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SELECT action FROM audit_log WHERE space_id = @space AND action LIKE 'sandbox.%' ORDER BY id", connection);
        command.Parameters.AddWithValue("space", spaceId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        List<string> actions = [];

        while (await reader.ReadAsync(Token))
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    #endregion
}
