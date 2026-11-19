namespace Drover.Server.Tests;

/// <summary>
/// Searching conversations through the API and the typed client, over real PostgreSQL with the real worker inside
/// the server: a person finds what they and their bots said, and nothing from a thread or a space they may not read.
/// </summary>
public sealed class ConversationSearchApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(script));

    #endregion

    #region Finding

    [Fact]
    public async Task A_person_finds_their_own_words_and_their_bots_reply_with_where_each_was_said()
    {
        script = new ScriptedModel([new TextOutput("The lighthouse keeper visits on Tuesdays.")], [new TextOutput("Noted.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        var side = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Errands"), HttpStatusCode.Created);
        await Ask(client, alice.SpaceId, main.Id, "When does the lighthouse keeper come?");
        await Ask(client, alice.SpaceId, side.Id, "Buy paint for the lighthouse.");

        var hits = await GetFor<List<MessageSearchHit>>(client, SearchPath(alice.SpaceId, "lighthouse"));

        hits.Should().HaveCount(3);
        hits.Should().OnlyContain(hit => hit.BotId == bot.Id && hit.BotName == "Helper");
        hits.Where(hit => hit.FromBot).Should().ContainSingle().Which.Should().Match<MessageSearchHit>(hit =>
            hit.ThreadId == main.Id && hit.InMainConversation && hit.Snippet.Contains("lighthouse keeper visits"));
        hits.Should().ContainSingle(hit => hit.ThreadId == side.Id).Which.Should().Match<MessageSearchHit>(hit =>
            !hit.InMainConversation && hit.ThreadTitle == "Errands" && !hit.FromBot);
        hits.Should().OnlyContain(hit => hit.Matches.Count > 0
            && hit.Matches.All(match => hit.Snippet.Substring(match.Start, match.Length).Equals("lighthouse", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task The_typed_client_finds_the_same_messages_as_the_api()
    {
        script = new ScriptedModel([new TextOutput("Saffron is in the left cupboard.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await CreateBot(client, alice.SpaceId);
        var main = await OpenMain(client, alice.SpaceId, bot.Id);
        await Ask(client, alice.SpaceId, main.Id, "Where is the saffron & salt?");
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };

        var throughApi = await GetFor<List<MessageSearchHit>>(client, SearchPath(alice.SpaceId, "saffron & salt"));
        var throughClient = await api.SearchMessages(alice.SpaceId, "saffron & salt", Token);

        throughClient.Should().BeEquivalentTo(throughApi);
        throughClient.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_words_and_more_than_two_hundred_characters_are_refused(string words)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var response = await client.Get(SearchPath(alice.SpaceId, words));
        var tooLong = await client.Get(SearchPath(alice.SpaceId, new string('a', 201)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("Say what to look for.");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Who Finds It

    [Fact]
    public async Task Nobody_else_finds_a_private_conversation_and_no_other_space_finds_it_at_all()
    {
        script = new ScriptedModel([new TextOutput("The marmalade recipe is safe.")]);
        var alice = await SeedPerson("alice");
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var outsider = await SeedPerson("outsider");
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId);
        var main = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        await Ask(asAlice, alice.SpaceId, main.Id, "Keep the marmalade recipe secret.");

        var alices = await GetFor<List<MessageSearchHit>>(asAlice, SearchPath(alice.SpaceId, "marmalade"));
        var admins = await GetFor<List<MessageSearchHit>>(asAdmin, SearchPath(alice.SpaceId, "marmalade"));
        var outsidersOwn = await GetFor<List<MessageSearchHit>>(asOutsider, SearchPath(outsider.SpaceId, "marmalade"));
        var outsiderInAlices = await asOutsider.Get(SearchPath(alice.SpaceId, "marmalade"));

        alices.Should().HaveCount(2);
        admins.Should().BeEmpty("an admin of the space cannot read a person's private thread");
        outsidersOwn.Should().BeEmpty("another space never sees this space's words");
        outsiderInAlices.IsSuccessStatusCode.Should().BeFalse("a person outside the space cannot search it");
        (await outsiderInAlices.Content.ReadAsStringAsync(Token)).Should().NotContain("marmalade");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string SearchPath(Guid spaceId, string words) => $"{Space(spaceId)}/threads/search?q={Uri.EscapeDataString(words)}";

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
