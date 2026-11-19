namespace Drover.Server.Tests;

/// <summary>
/// Saving a conversation through the API and the typed client, over real PostgreSQL with the real worker inside the
/// server: every message a person reads, in order, each tool call by name and outcome only, each file by name and
/// size, nothing a tool was given or gave back, and only to those who may read the thread.
/// </summary>
public sealed class ConversationExportApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region What It Holds

    [Fact]
    public async Task The_json_export_holds_every_message_in_order_with_tool_calls_summarized_and_no_secret()
    {
        // A key-shaped value made up for this test, sent to a tool as an argument and echoed in its output.
        var key = $"ghp_{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}";
        script = new ScriptedModel(
            [new TextOutput("Let me check."), new ToolCallOutput("c1", "time_now", $$"""{"time_zone":"{{key}}"}""")],
            [new ToolCallOutput("c2", "time_now")],
            [new TextOutput("It is noon.")],
            [new TextOutput("You are welcome.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var file = await Upload(client, alice.SpaceId, main.Id, "notes.txt", "one\ntwo");
        await Ask(client, alice.SpaceId, main.Id, new SendMessageRequest("What time is it?", FileIds: [file.Id]));
        await Ask(client, alice.SpaceId, main.Id, new SendMessageRequest("Thanks"));
        var stored = await GetFor<List<MessageResponse>>(client, $"{Space(alice.SpaceId)}/threads/{main.Id}/messages");
        var toolOutput = stored.SelectMany(message => message.Parts).Where(part => part.Kind == "tool_result").Select(part => part.Result!).ToList();

        var response = await client.Get(ExportPath(alice.SpaceId, main.Id, "json"));
        var body = await response.Content.ReadAsStringAsync(Token);
        var export = JsonSerializer.Deserialize<ConversationExport>(body, json)!;

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().MatchRegex(@"^Helper conversation \d{4}-\d{2}-\d{2}\.json$");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        export.Version.Should().Be(ConversationExport.CurrentVersion);
        export.Should().Match<ConversationExport>(found =>
            found.ThreadId == main.Id && found.BotId == bot.Id && found.BotName == "Helper" && found.IsMain && !found.IsShared);
        export.Messages.Select(message => (message.Role, message.Text)).Should().Equal(
            ("user", "What time is it?"),
            ("assistant", "Let me check."),
            ("assistant", string.Empty),
            ("assistant", "It is noon."),
            ("user", "Thanks"),
            ("assistant", "You are welcome."));
        export.Messages.Select(message => message.Position).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        export.Messages.Should().OnlyContain(message => !message.Pending && !message.Withheld && !message.SentBySchedule);
        export.Messages[0].Attachments.Should().Equal(new ExportedAttachment("notes.txt", "text/plain", 7));
        export.Messages.SelectMany(message => message.ToolCalls).Should().Equal(
            new ExportedToolCall("time_now", ExportedToolOutcomes.Succeeded),
            new ExportedToolCall("time_now", ExportedToolOutcomes.Succeeded));
        export.Messages.Should().NotContain(message => message.Role == "tool", "a tool's result is summarized on the call, not shown as a message");
        toolOutput.Should().HaveCount(2);
        body.Should().NotContain(key, "the arguments a tool was given are never exported");
        body.Should().NotContain("is not known", "nor is what a tool gave back");
        body.Should().NotContain("\"utc\"");
        body.Should().NotContainAny("one\ntwo", "one\\ntwo");
    }

    [Fact]
    public async Task The_markdown_export_reads_as_the_conversation_and_names_tools_and_files_without_their_content()
    {
        var key = $"ghp_{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}";
        script = new ScriptedModel(
            [new ToolCallOutput("c1", "time_now", $$"""{"time_zone":"{{key}}"}""")],
            [new TextOutput("It is **noon**.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var file = await Upload(client, alice.SpaceId, main.Id, "my_notes [draft].txt", "secret plans");
        await Ask(client, alice.SpaceId, main.Id, new SendMessageRequest("What time is it?", FileIds: [file.Id]));

        var response = await client.Get(ExportPath(alice.SpaceId, main.Id, null));
        var text = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/markdown", "Markdown is the default");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".md");
        text.Should().StartWith("# Conversation with Helper\n");
        text.Should().Contain("Export format version 1.");
        text.IndexOf("What time is it?", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("`time_now`: succeeded", StringComparison.Ordinal))
            .And.BeLessThan(text.IndexOf("It is **noon**.", StringComparison.Ordinal));
        text.Should().Contain("## Person, ").And.Contain("## Helper, ");
        text.Should().Contain(@"- my\_notes \[draft\].txt (text/plain, 12 bytes)", "a file's name is listed as plain text");
        text.Should().NotContain(key).And.NotContain("is not known").And.NotContain("secret plans").And.NotContain("\"utc\"");
    }

    [Fact]
    public async Task A_call_waiting_for_approval_and_one_declined_are_exported_with_those_outcomes()
    {
        script = new ScriptedModel([new ToolCallOutput("c1", "time_now")], [new TextOutput("I could not check.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        await AskBeforeEveryTool(alice.SpaceId, alice.UserId, bot.Id);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var sent = await PostFor<SendMessageResponse>(
            client, $"{Space(alice.SpaceId)}/threads/{main.Id}/messages", new SendMessageRequest("What time is it?"), HttpStatusCode.Accepted);
        await WaitForRun(client, alice.SpaceId, sent.Run.Id, "waiting");

        var waiting = await GetFor<ConversationExport>(client, ExportPath(alice.SpaceId, main.Id, "json"));
        var call = (await GetFor<List<MessageResponse>>(client, $"{Space(alice.SpaceId)}/threads/{main.Id}/messages"))
            .SelectMany(message => message.Parts).Single(part => part.Kind == "tool_call");
        (await client.Post($"{Space(alice.SpaceId)}/tool-calls/{call.ToolCallId}/decision", new ToolDecisionRequest(false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await WaitForRun(client, alice.SpaceId, sent.Run.Id, "completed");
        var declined = await GetFor<ConversationExport>(client, ExportPath(alice.SpaceId, main.Id, "json"));

        waiting.Messages.SelectMany(message => message.ToolCalls).Should().Equal(new ExportedToolCall("time_now", ExportedToolOutcomes.AwaitingApproval));
        declined.Messages.SelectMany(message => message.ToolCalls).Should().Equal(new ExportedToolCall("time_now", ExportedToolOutcomes.Denied));
        declined.Messages[^1].Text.Should().Be("I could not check.");
    }

    #endregion

    #region Who Saves It

    [Fact]
    public async Task Only_who_may_read_the_thread_can_export_it_and_an_unknown_format_is_refused()
    {
        script = new ScriptedModel([new TextOutput("They planned the week.")]);
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var outsider = await SeedPerson("outsider");
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId);
        var main = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        await Ask(asAlice, alice.SpaceId, main.Id, new SendMessageRequest("Plan my week"));

        var owner = await asAlice.Get(ExportPath(alice.SpaceId, main.Id, "markdown"));
        var byMember = await asMember.Get(ExportPath(alice.SpaceId, main.Id, "markdown"));
        var byAdmin = await asAdmin.Get(ExportPath(alice.SpaceId, main.Id, "json"));
        var byOutsider = await asOutsider.Get(ExportPath(alice.SpaceId, main.Id, "json"));
        var outsiderInOwnSpace = await asOutsider.Get(ExportPath(outsider.SpaceId, main.Id, "json"));
        var unknown = await asAlice.Get(ExportPath(alice.SpaceId, Guid.CreateVersion7(), "json"));
        var badFormat = await asAlice.Get(ExportPath(alice.SpaceId, main.Id, "pdf"));

        owner.StatusCode.Should().Be(HttpStatusCode.OK);
        byMember.StatusCode.Should().Be(HttpStatusCode.NotFound, "a member cannot read another member's private thread");
        byAdmin.StatusCode.Should().Be(HttpStatusCode.NotFound, "nor can an admin: exporting the thread is reading it");
        byOutsider.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderInOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        badFormat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var refused in new[] { byMember, byAdmin, byOutsider, outsiderInOwnSpace })
        {
            (await refused.Content.ReadAsStringAsync(Token)).Should().NotContain("planned").And.NotContain("Plan my week");
        }
    }

    [Fact]
    public async Task The_typed_client_saves_the_file_the_server_wrote_under_its_name()
    {
        script = new ScriptedModel([new TextOutput("Answer one.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        await Ask(client, alice.SpaceId, main.Id, new SendMessageRequest("one"));
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };

        var markdown = await api.ExportConversation(alice.SpaceId, main.Id, ExportFormats.Markdown, Token);
        var asJson = await api.ExportConversation(alice.SpaceId, main.Id, ExportFormats.Json, Token);
        var parsed = JsonSerializer.Deserialize<ConversationExport>(asJson.Text, json)!;

        markdown.Name.Should().MatchRegex(@"^Helper conversation \d{4}-\d{2}-\d{2}\.md$");
        markdown.MediaType.Should().Be("text/markdown");
        markdown.Text.Should().Contain("Answer one.");
        asJson.Name.Should().EndWith(".json");
        asJson.MediaType.Should().Be("application/json");
        parsed.Messages.Select(message => message.Text).Should().Equal("one", "Answer one.");
        await api.Invoking(found => found.ExportConversation(alice.SpaceId, Guid.CreateVersion7(), ExportFormats.Json, Token))
            .Should().ThrowAsync<DroverApiException>().Where(refusal => refusal.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string ExportPath(Guid spaceId, Guid threadId, string? format) =>
        $"{Space(spaceId)}/threads/{threadId}/export{(format is null ? string.Empty : $"?format={format}")}";

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"),
            HttpStatusCode.Created);
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    private static async Task<ThreadSummary> OpenMain(ApiClient client, Guid spaceId, Guid botId)
    {
        var response = await client.Put($"{Space(spaceId)}/bots/{botId}/main-thread");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ThreadSummary>(Token))!;
    }

    private static async Task<FileResponse> Upload(ApiClient client, Guid spaceId, Guid threadId, string name, string text)
    {
        var response = await client.PostContent(
            $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}",
            new ByteArrayContent(Encoding.UTF8.GetBytes(text)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileResponse>(Token))!;
    }

    /// <summary>Sends one message to the thread and waits for the run that answers it to complete.</summary>
    private static async Task Ask(ApiClient client, Guid spaceId, Guid threadId, SendMessageRequest message)
    {
        var sent = await PostFor<SendMessageResponse>(client, $"{Space(spaceId)}/threads/{threadId}/messages", message, HttpStatusCode.Accepted);
        var run = await WaitForRun(client, spaceId, sent.Run.Id, "completed", "failed");
        run.Status.Should().Be("completed", run.Error?.Message);
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId, params string[] statuses)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{runId}");

            if (statuses.Contains(run.Status))
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should reach {string.Join(" or ", statuses)}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    /// <summary>Every tool of the bot asks a person first, as a bot editor sets it.</summary>
    private async Task AskBeforeEveryTool(Guid spaceId, Guid userId, Guid botId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var changed = await scope.ServiceProvider.GetRequiredService<DroverDbContext>().ToolGrants
            .Where(grant => grant.BotId == botId)
            .ExecuteUpdateAsync(grant => grant.SetProperty(row => row.Effect, GrantEffect.Ask), Token);
        changed.Should().BeGreaterThan(0);
        await work.Commit(Token);
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    #endregion
}
