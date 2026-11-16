namespace Drover.Server.Tests;

/// <summary>
/// Files a bot shares from its sandbox, over the API: the reply carries the file after a reload, the file is served so
/// nothing in it runs, and the list of files exchanged with a bot holds both directions for the person and nothing of
/// their private conversation for anyone else.
/// </summary>
public sealed class SharedFileApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private readonly MemorySandboxProvider sandboxes = new();

    private readonly ScriptedModel script = new(
        [new ToolCallOutput("c1", "sandbox_write_file", """{"path":"out/page.html","content":"<script>alert(document.cookie)</script>"}""")],
        [new ToolCallOutput("c2", "sandbox_share_file", """{"path":"out/page.html","caption":"Your page"}""")],
        [new TextOutput("Here it is.")],
        [new TextOutput("Thanks for the notes.")]);

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(script));
        services.AddSingleton<ISandboxProvider>(sandboxes);
        services.AddSandboxTools();
    }

    #endregion

    #region Tests

    [Fact]
    public async Task A_shared_file_is_in_the_bots_reply_after_a_reload_and_html_from_a_bot_is_never_served_as_a_page()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);

        var sent = await Read<SendMessageResponse>(await client.Post($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest("Make me a page")));
        (await WaitForRun(client, spaceId, sent.Run.Id)).Status.Should().Be("completed");

        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(spaceId)}/threads/{threadId}/messages"));
        var file = messages.Where(message => message.Role == "assistant").SelectMany(message => message.Parts).Single(part => part.Kind == "file").File!;
        file.Should().BeEquivalentTo(new { Name = "page.html", ContentType = "text/html", Kind = FileKinds.Text, Caption = "Your page" }, options => options.ExcludingMissingMembers());

        var shown = await client.Get($"{Space(spaceId)}/files/{file.Id}");
        shown.StatusCode.Should().Be(HttpStatusCode.OK);
        shown.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8", "a page a bot wrote is shown as its text, never rendered");
        ShouldBeSafe(shown);
        var saved = await client.Get($"{Space(spaceId)}/files/{file.Id}?download=true");
        saved.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        saved.Content.Headers.ContentDisposition.FileNameStar.Should().Be("page.html");
        ShouldBeSafe(saved);
    }

    [Fact]
    public async Task The_files_list_holds_both_directions_newest_first_for_the_person_and_nothing_of_theirs_for_anyone_else()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var made = await Read<SendMessageResponse>(await client.Post($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest("Make me a page")));
        (await WaitForRun(client, spaceId, made.Run.Id)).Status.Should().Be("completed");
        Time.Advance(TimeSpan.FromMinutes(1));
        var notes = await Read<FileResponse>(await client.PostContent(
            $"{Space(spaceId)}/threads/{threadId}/files?name=notes.txt",
            new ByteArrayContent(Encoding.UTF8.GetBytes("my notes"))));
        var sent = await Read<SendMessageResponse>(await client.Post(
            $"{Space(spaceId)}/threads/{threadId}/messages",
            new SendMessageRequest("My notes", FileIds: [notes.Id])));
        (await WaitForRun(client, spaceId, sent.Run.Id)).Status.Should().Be("completed");
        var member = await SeedMember(spaceId, "bob", MembershipRole.Admin);
        var admin = await SignIn(member.Email, member.Password);
        var (stranger, strangerSpace, _, _) = await Conversation("eve");

        var listed = await Read<List<ExchangedFileResponse>>(await client.Get($"{Space(spaceId)}/bots/{botId}/files"));

        listed.Select(exchanged => (exchanged.File.Name, exchanged.FromBot, exchanged.ThreadId, exchanged.InMainConversation))
            .Should().Equal(("notes.txt", false, threadId, true), ("page.html", true, threadId, true));
        listed[0].MessageId.Should().Be(sent.Message.Id, "a file links to the moment it was sent");
        (await Read<List<ExchangedFileResponse>>(await admin.Get($"{Space(spaceId)}/bots/{botId}/files")))
            .Should().BeEmpty("an admin cannot see a member's private conversation, so none of its files");
        (await admin.Get($"{Space(spaceId)}/files/{listed[1].File.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get($"{Space(spaceId)}/bots/{botId}/files")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the stranger is not a member");
        (await stranger.Get($"{Space(strangerSpace)}/bots/{botId}/files")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the bot is not in the stranger's space");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static void ShouldBeSafe(HttpResponseMessage response)
    {
        var policy = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        policy.Should().Contain("sandbox").And.Contain("default-src 'none'").And.NotContain("script-src");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().Equal("same-origin");
    }

    private async Task<(ApiClient Client, Guid SpaceId, Guid ThreadId, Guid BotId)> Conversation(string name)
    {
        var person = await SeedPerson(name);
        var client = await SignIn(person.Email, person.Password);
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{Space(person.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(person.SpaceId)}/bots/{bot.Id}/main-thread"));
        return (client, person.SpaceId, thread.Id, bot.Id);
    }

    /// <summary>A private sandbox and the grants to write a file in it and share one, as a person gives them in the bot editor.</summary>
    private static async Task GiveSandbox(ApiClient client, Guid spaceId, Guid botId)
    {
        var saved = await client.Put(
            $"{Space(spaceId)}/bots/{botId}/definition",
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: false), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var tools = await Read<List<BotToolEntry>>(await client.Get($"{Space(spaceId)}/bots/{botId}/tools"));

        foreach (var tool in tools.Where(tool => tool.Name is "sandbox.write_file" or "sandbox.share_file"))
        {
            (await client.Put($"{Space(spaceId)}/bots/{botId}/tools/{tool.ToolId}", new SetToolGrantRequest("allow"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await Read<RunSummary>(await client.Get($"{Space(spaceId)}/runs/{runId}"));

            if (run.Status is "completed" or "failed" or "cancelled")
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should finish; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    #endregion
}
