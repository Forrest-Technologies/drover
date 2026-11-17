namespace Drover.Server.Tests;

/// <summary>
/// The running summary of a main conversation through the API and the typed client, over real PostgreSQL with the
/// real worker inside the server: empty until one is written, then the summary the server wrote, readable only by who
/// may read the thread. Here a summary is due once four person and bot messages lie beyond the last one.
/// </summary>
public sealed class ConversationSummaryApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings)
        {
            ["Runs:SummaryAfterMessages"] = "4",
            ["Runs:SummaryKeepsRecentMessages"] = "2",
        };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Reading

    [Fact]
    public async Task A_main_thread_has_an_empty_summary_until_the_server_writes_one_and_then_serves_it()
    {
        script = new ScriptedModel([new TextOutput("Answer one.")], [new TextOutput("Answer two.")], [new TextOutput("They planned the week.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);

        var before = await GetFor<ConversationSummary>(client, SummaryPath(alice.SpaceId, main.Id));
        await Ask(client, alice.SpaceId, main.Id, "one");
        await Ask(client, alice.SpaceId, main.Id, "two");
        var after = await WaitForSummary(client, alice.SpaceId, main.Id);

        before.Should().Be(new ConversationSummary(null, 0, null));
        after.Text.Should().Be("They planned the week.");
        after.MessagesCovered.Should().Be(2);
        after.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_typed_client_reads_the_same_summary_as_the_api()
    {
        script = new ScriptedModel([new TextOutput("Answer one.")], [new TextOutput("Answer two.")], [new TextOutput("They planned the week.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };
        await Ask(client, alice.SpaceId, main.Id, "one");
        await Ask(client, alice.SpaceId, main.Id, "two");
        var throughApi = await WaitForSummary(client, alice.SpaceId, main.Id);

        var throughClient = await api.GetConversationSummary(alice.SpaceId, main.Id, Token);

        throughClient.Should().Be(throughApi);
    }

    [Fact]
    public async Task A_side_chat_returns_the_empty_summary()
    {
        script = new ScriptedModel([new TextOutput("Answer one.")], [new TextOutput("Answer two.")], [new TextOutput("Answer three.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var side = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);

        await Ask(client, alice.SpaceId, side.Id, "one");
        await Ask(client, alice.SpaceId, side.Id, "two");
        await Ask(client, alice.SpaceId, side.Id, "three");
        var summary = await GetFor<ConversationSummary>(client, SummaryPath(alice.SpaceId, side.Id));

        summary.Should().Be(new ConversationSummary(null, 0, null));
        model!.Requests.Should().HaveCount(3, "a side chat is never summarized");
    }

    #endregion

    #region Who Reads It

    [Fact]
    public async Task Only_the_person_who_may_read_the_thread_reads_its_summary()
    {
        script = new ScriptedModel([new TextOutput("Answer one.")], [new TextOutput("Answer two.")], [new TextOutput("They planned the week.")]);
        var alice = await SeedPerson("alice");
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var outsider = await SeedPerson("outsider");
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId);
        var main = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        await Ask(asAlice, alice.SpaceId, main.Id, "one");
        await Ask(asAlice, alice.SpaceId, main.Id, "two");
        await WaitForSummary(asAlice, alice.SpaceId, main.Id);

        var adminReads = await asAdmin.Get(SummaryPath(alice.SpaceId, main.Id));
        var adminReadsThread = await asAdmin.Get($"{Space(alice.SpaceId)}/threads/{main.Id}");
        var outsiderReads = await asOutsider.Get(SummaryPath(alice.SpaceId, main.Id));
        var outsiderInOwnSpace = await asOutsider.Get(SummaryPath(outsider.SpaceId, main.Id));
        var unknown = await asAlice.Get(SummaryPath(alice.SpaceId, Guid.CreateVersion7()));

        adminReads.StatusCode.Should().Be(adminReadsThread.StatusCode, "the summary is read exactly as the thread is");
        adminReads.StatusCode.Should().Be(HttpStatusCode.NotFound, "an admin of the space cannot read a person's private thread");
        outsiderReads.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderInOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminReads.Content.ReadAsStringAsync(Token)).Should().NotContain("planned");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string SummaryPath(Guid spaceId, Guid threadId) => $"{Space(spaceId)}/threads/{threadId}/summary";

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

    /// <summary>Sends one message to the thread and waits for the run that answers it to complete.</summary>
    private static async Task Ask(ApiClient client, Guid spaceId, Guid threadId, string text)
    {
        var sent = await PostFor<SendMessageResponse>(client, $"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest(text), HttpStatusCode.Accepted);
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{sent.Run.Id}");

            if (run.Status is "completed" or "failed")
            {
                run.Status.Should().Be("completed", run.Error?.Message);
                return;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {sent.Run.Id} should end; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private static async Task<ConversationSummary> WaitForSummary(ApiClient client, Guid spaceId, Guid threadId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var summary = await GetFor<ConversationSummary>(client, SummaryPath(spaceId, threadId));

            if (summary.Text is not null)
            {
                return summary;
            }

            (watch.Elapsed < patience).Should().BeTrue($"thread {threadId} should get a summary");
            await Task.Delay(25, Token);
        }
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
