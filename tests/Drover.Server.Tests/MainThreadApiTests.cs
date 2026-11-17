namespace Drover.Server.Tests;

/// <summary>
/// Each person's one main conversation with a bot, through the API over real PostgreSQL: made on first use, the same
/// thread every time after, private to its person, safe when opened twice at once, and never given a title.
/// </summary>
public sealed class MainThreadApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Opening

    [Fact]
    public async Task Opening_the_main_thread_makes_it_once_and_returns_the_same_thread_after()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);

        var opened = await OpenMain(client, alice.SpaceId, bot.Id);
        var again = await OpenMain(client, alice.SpaceId, bot.Id);
        var side = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Side"), HttpStatusCode.Created);
        var listed = await GetFor<List<ThreadSummary>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads");
        var read = await GetFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/threads/{opened.Id}");

        opened.Should().Match<ThreadSummary>(thread =>
            thread.IsMain && thread.BotId == bot.Id && thread.Title == null && !thread.IsShared && thread.ActiveRunId == null);
        again.Should().Be(opened);
        side.IsMain.Should().BeFalse("a thread made with the threads route is a side chat");
        listed.Select(thread => (thread.Id, thread.IsMain)).Should().BeEquivalentTo([(opened.Id, true), (side.Id, false)]);
        read.IsMain.Should().BeTrue();
    }

    [Fact]
    public async Task The_typed_client_opens_the_same_main_thread_as_the_api()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };

        var throughClient = await api.OpenMainThread(alice.SpaceId, bot.Id, Token);
        var throughApi = await OpenMain(client, alice.SpaceId, bot.Id);

        throughClient.Should().Be(throughApi);
        throughClient.IsMain.Should().BeTrue();
    }

    [Fact]
    public async Task Two_requests_that_open_the_main_thread_at_once_get_the_same_thread()
    {
        var alice = await SeedPerson("alice");
        var first = await SignIn(alice);
        var bot = await CreateBot(first, alice.SpaceId);
        List<ApiClient> clients = [first];

        for (var index = 1; index < 6; index++)
        {
            clients.Add(await SignIn(alice));
        }

        var opened = await Task.WhenAll(clients.Select(client => OpenMain(client, alice.SpaceId, bot.Id)));
        var listed = await GetFor<List<ThreadSummary>>(first, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads");

        opened.Select(thread => thread.Id).Distinct().Should().ContainSingle();
        listed.Should().ContainSingle(thread => thread.IsMain).Which.Id.Should().Be(opened[0].Id);
        listed.Should().ContainSingle("the requests that lost made nothing");
    }

    #endregion

    #region Who Sees It

    [Fact]
    public async Task Each_person_has_their_own_main_thread_and_cannot_read_anothers()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider);
        var bot = await CreateBot(asAlice, alice.SpaceId);

        var alicesMain = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var membersMain = await OpenMain(asMember, alice.SpaceId, bot.Id);
        var memberReadsAlices = await asMember.Get($"{Space(alice.SpaceId)}/threads/{alicesMain.Id}");
        var memberLists = await GetFor<List<ThreadSummary>>(asMember, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads");
        var outsiderOpens = await asOutsider.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/main-thread");
        var outsiderInOwnSpace = await asOutsider.Put($"{Space(outsider.SpaceId)}/bots/{bot.Id}/main-thread");

        membersMain.Id.Should().NotBe(alicesMain.Id);
        membersMain.IsMain.Should().BeTrue();
        memberReadsAlices.StatusCode.Should().Be(HttpStatusCode.NotFound);
        memberLists.Should().ContainSingle().Which.Id.Should().Be(membersMain.Id);
        outsiderOpens.StatusCode.Should().Be(HttpStatusCode.NotFound, "a person outside the space reaches nothing in it");
        outsiderInOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "a bot of another space is not a bot here");
        (await OpenMain(asAlice, alice.SpaceId, bot.Id)).Should().Be(alicesMain, "the other person's main thread changed nothing");
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task A_bot_that_cannot_be_talked_to_gives_no_new_main_thread_but_an_existing_one_still_opens()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var talkedTo = await CreateBot(client, alice.SpaceId, "Talked to");
        var neverOpened = await CreateBot(client, alice.SpaceId, "Never opened");
        var existing = await OpenMain(client, alice.SpaceId, talkedTo.Id);
        (await client.Post($"{Space(alice.SpaceId)}/bots/{talkedTo.Id}/pause")).EnsureSuccessStatusCode();
        (await client.Post($"{Space(alice.SpaceId)}/bots/{neverOpened.Id}/pause")).EnsureSuccessStatusCode();

        var reopened = await OpenMain(client, alice.SpaceId, talkedTo.Id);
        var refused = await client.Put($"{Space(alice.SpaceId)}/bots/{neverOpened.Id}/main-thread");
        var sideChat = await client.Post($"{Space(alice.SpaceId)}/bots/{neverOpened.Id}/threads", new CreateThreadRequest(null));
        var unknown = await client.Put($"{Space(alice.SpaceId)}/bots/{Guid.CreateVersion7()}/main-thread");

        reopened.Should().Be(existing);
        refused.StatusCode.Should().Be(sideChat.StatusCode, "the same refusal as making any thread with that bot");
        refused.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Titles

    [Fact]
    public async Task A_main_thread_takes_no_title_from_its_first_message_and_a_side_chat_does()
    {
        script = new ScriptedModel([new TextOutput("Hello.")], [new TextOutput("Hello again.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var side = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);

        await Ask(client, alice.SpaceId, main.Id, "Plan my week");
        await Ask(client, alice.SpaceId, side.Id, "Plan my weekend");
        var mainAfter = await GetFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/threads/{main.Id}");
        var sideAfter = await GetFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/threads/{side.Id}");

        mainAfter.Title.Should().BeNull();
        sideAfter.Title.Should().Be("Plan my weekend");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) => SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, string name = "Helper")
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, $"For {name}", null, null, "scripted"),
            HttpStatusCode.Created);
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    private static async Task<ThreadSummary> OpenMain(ApiClient client, Guid spaceId, Guid botId)
    {
        var response = await client.Put($"{Space(spaceId)}/bots/{botId}/main-thread");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ThreadSummary>(Token))!;
    }

    /// <summary>Sends one message to the thread and waits for the run that answers it to end.</summary>
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
