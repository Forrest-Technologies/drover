namespace Drover.Server.Tests;

/// <summary>
/// Tool connections through the API, end to end: the real adapter, a real tool server on loopback, the real worker
/// and agent runtime, and the scripted model in place of a provider.
/// </summary>
public sealed class ToolConnectionApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Tests

    [Theory]
    [InlineData(ServerGeneration.Stateless, "stateless")]
    [InlineData(ServerGeneration.SessionBased, "session-based")]
    public async Task An_admin_connects_a_server_with_a_header_and_a_bot_uses_a_granted_tool_with_the_secret_nowhere_but_the_server(
        ServerGeneration generation,
        string badge)
    {
        script = new ScriptedModel([new ToolCallOutput("c1", "helpdesk__echo", """{"text":"hello"}""")], [new TextOutput("It said hello.")]);
        var secret = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(generation, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var client = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var request = new CreateToolConnectionRequest("Helpdesk", server.Url.ToString(), "header", HeaderName, secret, "trusted");

        var byMember = await asMember.Post($"{space}/tool-connections", request);
        var created = await client.Post($"{space}/tool-connections", request);
        var createdBody = await created.Content.ReadAsStringAsync(Token);
        var listed = JsonSerializer.Deserialize<ToolConnectionRefresh>(createdBody, JsonSerializerOptions.Web)!;
        var seenByMember = await asMember.Get($"{space}/tool-connections");
        var toolsBody = await (await asMember.Get($"{space}/tool-connections/{listed.Connection.Id}/tools")).Content.ReadAsStringAsync(Token);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only an admin adds a connection");
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
        listed.Problem.Should().BeNull();
        listed.Connection.Should().Match<ToolConnectionSummary>(connection =>
            connection.Slug == "helpdesk" && connection.Status == "ready" && connection.AuthKind == "header" && connection.Trust == "trusted"
            && connection.NegotiatedProtocol!.StartsWith(badge));
        listed.Tools.Select(tool => tool.ModelName).Should().BeEquivalentTo(["helpdesk__echo", "helpdesk__record", "helpdesk__fail", "helpdesk__slow"]);
        listed.Tools.Should().OnlyContain(tool => tool.Sensitivity == "write", "a server's claim that a tool only reads decides nothing");
        listed.Added.Should().Be(4);
        seenByMember.StatusCode.Should().Be(HttpStatusCode.OK, "any member may see which connections exist");

        var bot = await CreateBot(client, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");
        var thread = await PostFor<ThreadSummary>(client, $"{space}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var sent = await PostFor<SendMessageResponse>(client, $"{space}/threads/{thread.Id}/messages", new SendMessageRequest("Say hello"));
        var run = await WaitForRun(client, alice.SpaceId, sent.Run.Id);
        var messages = await (await client.Get($"{space}/threads/{thread.Id}/messages")).Content.ReadAsStringAsync(Token);
        var events = await (await client.Get($"{space}/runs/{sent.Run.Id}/events")).Content.ReadAsStringAsync(Token);

        run.Status.Should().Be("completed", run.Error?.Message);
        model!.ToolsOffered[0].Should().Contain("helpdesk__echo").And.NotContain("helpdesk__record", "a tool nobody granted is not sent to the model");
        script.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"echo: hello\""));
        var everythingShown = string.Join('\n', createdBody, toolsBody, messages, events, await seenByMember.Content.ReadAsStringAsync(Token), string.Join('\n', Logs.Entries.Select(entry => entry.ToString())));
        everythingShown.Should().NotContain(secret, "a header value is never in an API response, an event, or a log");
    }

    [Fact]
    public async Task A_refused_credential_is_reported_in_plain_words_and_a_new_header_fixes_it()
    {
        var secret = NewPassword();
        var wrong = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";

        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Helpdesk", server.Url.ToString(), "header", HeaderName, wrong));
        var replaced = await client.Put($"{space}/tool-connections/{created.Connection.Id}/header", new ReplaceToolHeaderRequest(HeaderName, secret));
        var refreshed = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections/{created.Connection.Id}/refresh", new { });

        created.Problem.Should().Contain("refused the credential").And.NotContain(wrong);
        created.Connection.Status.Should().Be("failed");
        created.Connection.LastError.Should().NotContain(wrong);
        created.Tools.Should().BeEmpty();
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replaced.Content.ReadAsStringAsync(Token)).Should().NotContain(secret);
        refreshed.Problem.Should().BeNull();
        refreshed.Connection.Status.Should().Be("ready");
        refreshed.Tools.Should().HaveCount(4);
    }

    [Fact]
    public async Task A_connection_is_validated_removed_by_an_admin_only_and_invisible_from_another_space()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var outsider = await SeedPerson("outsider");
        var client = await SignIn(alice.Email, alice.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";

        var noAddress = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", "not a url"));
        var withPassword = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", "https://user:pass@tools.example.test/"));
        var reservedHeader = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", server.Url.ToString(), "header", "Host", "evil.example.test"));
        var signIn = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Later", server.Url.ToString(), "oauth"));
        var unknownSetting = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", server.Url.ToString(), Trust: "very"));
        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Notes", server.Url.ToString()));
        var id = created.Connection.Id;
        var fromOtherSpace = await asOutsider.Get($"{space}/tool-connections/{id}/tools");
        var inOwnSpace = await asOutsider.Get($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}/tools");
        var removedFromOwnSpace = await asOutsider.Delete($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}");
        var updated = await client.Put($"{space}/tool-connections/{id}", new UpdateToolConnectionRequest("trusted", "stateless_only"));
        var removed = await client.Delete($"{space}/tool-connections/{id}");
        var afterwards = await client.Get($"{space}/tool-connections/{id}/tools");

        noAddress.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a password in the address would be stored and shown in the clear");
        reservedHeader.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a connection may not set a header that belongs to the transport");
        signIn.StatusCode.Should().Be(HttpStatusCode.BadRequest, "sign-in to a tool server is not built yet, and says so");
        unknownSetting.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        created.Connection.Trust.Should().Be("untrusted", "a connection is not trusted until a person says so");
        fromOtherSpace.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        inOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "a connection id from another space finds nothing in one's own");
        removedFromOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await updated.Content.ReadFromJsonAsync<ToolConnectionSummary>(Token))!.Should().Match<ToolConnectionSummary>(connection =>
            connection.Trust == "trusted" && connection.ProtocolMode == "stateless_only" && connection.NegotiatedProtocol == null);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterwards.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tool_server_on_a_private_address_is_refused_when_the_space_does_not_allow_them()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId, allowed: false);

        var created = await PostFor<ToolConnectionRefresh>(
            client,
            $"/api/spaces/{alice.SpaceId}/tool-connections",
            new CreateToolConnectionRequest("Inside", server.Url.ToString()));

        created.Problem.Should().Contain("private addresses", "a tool server is reached through the checked outbound path, like a model");
        created.Connection.Status.Should().Be("failed");
        created.Tools.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>The tool server of these tests listens on loopback, which a space must allow.</summary>
    private async Task AllowPrivateAddresses(Guid spaceId, bool allowed = true)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, Guid.CreateVersion7());
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var space = await scope.ServiceProvider.GetRequiredService<DroverDbContext>().Spaces.SingleAsync(row => row.Id == spaceId, Token);
        space.Settings = new SpaceSettings { AllowPrivateAddresses = allowed };
        await work.Commit(Token);
    }

    /// <summary>Grants the bot one of the connection's tools, as a person would in the bot editor.</summary>
    private async Task Grant(Guid spaceId, Guid userId, Guid botId, string toolName)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var tool = await db.Tools.SingleAsync(found => found.Name == toolName, Token);
        db.ToolGrants.Add(new ToolGrant { BotId = botId, ToolId = tool.Id, Effect = GrantEffect.Allow, GrantedByUserId = userId });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"/api/spaces/{spaceId}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        return await PostFor<BotSummary>(client, $"/api/spaces/{spaceId}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null));
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Post(path, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = (await (await client.Get($"/api/spaces/{spaceId}/runs/{runId}")).Content.ReadFromJsonAsync<RunSummary>(Token))!;

            if (run.Status is "completed" or "failed" or "cancelled" or "waiting")
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should finish; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    #endregion
}
