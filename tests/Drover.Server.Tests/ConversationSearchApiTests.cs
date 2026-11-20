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

        var hits = await Search(client, alice.SpaceId, "lighthouse");

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

        var throughApi = await Search(client, alice.SpaceId, "saffron & salt");
        var throughClient = await api.SearchMessages(alice.SpaceId, "saffron & salt", cancellationToken: Token);

        throughClient.Hits.Should().BeEquivalentTo(throughApi);
        throughClient.Hits.Should().NotBeEmpty();
        throughClient.Window.Should().Be(MessageRepository.SearchCandidateLimit);
        throughClient.Older.Should().BeNull("a small history is searched all at once");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_words_and_more_than_two_hundred_characters_are_refused(string words)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var response = await client.Post(SearchPath(alice.SpaceId), new SearchMessagesRequest(words));
        var tooLong = await client.Post(SearchPath(alice.SpaceId), new SearchMessagesRequest(new string('a', 201)));
        using var oversizedBody = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new SearchMessagesRequest(new string('a', 5000))),
            System.Text.Encoding.UTF8,
            "application/json");
        var pastBodyLimit = await client.PostContent(SearchPath(alice.SpaceId), oversizedBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("Say what to look for.");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        pastBodyLimit.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
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

        var alices = await Search(asAlice, alice.SpaceId, "marmalade");
        var admins = await Search(asAdmin, alice.SpaceId, "marmalade");
        var outsidersOwn = await Search(asOutsider, outsider.SpaceId, "marmalade");
        var outsiderInAlices = await asOutsider.Post(SearchPath(alice.SpaceId), new SearchMessagesRequest("marmalade"));

        alices.Should().HaveCount(2);
        admins.Should().BeEmpty("an admin of the space cannot read a person's private thread");
        outsidersOwn.Should().BeEmpty("another space never sees this space's words");
        outsiderInAlices.IsSuccessStatusCode.Should().BeFalse("a person outside the space cannot search it");
        (await outsiderInAlices.Content.ReadAsStringAsync(Token)).Should().NotContain("marmalade");
    }

    #endregion

    #region Limits

    [Fact]
    public async Task Concurrent_searches_past_the_limit_are_refused_with_429()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var limiter = Services.GetRequiredService<ConversationSearchLimiter>();

        List<IDisposable> held = [];
        try
        {
            for (var index = 0; index < ConversationSearchLimiter.MaxPerUser; index++)
            {
                held.Add(limiter.TryAcquire(alice.SpaceId, alice.UserId)!);
            }

            held.Should().NotContainNulls();

            var refused = await client.Post(SearchPath(alice.SpaceId), new SearchMessagesRequest("lighthouse"));
            refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            foreach (var lease in held)
            {
                lease.Dispose();
            }
        }

        var allowed = await client.Post(SearchPath(alice.SpaceId), new SearchMessagesRequest("lighthouse"));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void The_limiter_bounds_per_user_per_space_and_global_concurrent_searches()
    {
        var limiter = new ConversationSearchLimiter();
        var spaceA = Guid.CreateVersion7();
        var user1 = Guid.CreateVersion7();
        var user2 = Guid.CreateVersion7();
        var user3 = Guid.CreateVersion7();
        List<IDisposable> leases = [];

        try
        {
            using var u1a = limiter.TryAcquire(spaceA, user1);
            using var u1b = limiter.TryAcquire(spaceA, user1);
            u1a.Should().NotBeNull();
            u1b.Should().NotBeNull();
            limiter.TryAcquire(spaceA, user1).Should().BeNull("a single user may hold at most MaxPerUser");

            using var u2a = limiter.TryAcquire(spaceA, user2);
            using var u2b = limiter.TryAcquire(spaceA, user2);
            u2a.Should().NotBeNull();
            u2b.Should().NotBeNull();
            limiter.TryAcquire(spaceA, user3).Should().BeNull("one space may hold at most MaxPerSpace");

            u1b!.Dispose();
            using var u3a = limiter.TryAcquire(spaceA, user3);
            u3a.Should().NotBeNull("releasing a lease frees both the user and space slots");

            while (leases.Count < ConversationSearchLimiter.MaxGlobal - ConversationSearchLimiter.MaxPerSpace)
            {
                var lease = limiter.TryAcquire(Guid.CreateVersion7(), Guid.CreateVersion7());
                lease.Should().NotBeNull();
                leases.Add(lease!);
            }

            limiter.TryAcquire(Guid.CreateVersion7(), Guid.CreateVersion7()).Should().BeNull("the server caps total concurrent searches");
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string SearchPath(Guid spaceId) => $"{Space(spaceId)}/threads/search";

    private static async Task<List<MessageSearchHit>> Search(ApiClient client, Guid spaceId, string words) =>
        [.. (await PostFor<MessageSearchResult>(client, SearchPath(spaceId), new SearchMessagesRequest(words), HttpStatusCode.OK)).Hits];

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

/// <summary>Verifies that HTTP request routing and SQL command logs do not record searched words when enabled.</summary>
public sealed class ConversationSearchLoggingTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Logging:LogLevel:Microsoft.AspNetCore.Routing"] = "Information",
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task Request_diagnostics_omit_the_search_words()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        const string SecretPhrase = "sensitive-search-phrase-98765";

        var response = await client.Post($"/api/spaces/{alice.SpaceId}/threads/search", new SearchMessagesRequest(SecretPhrase));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Logs.Entries.Should().Contain(entry =>
            entry.Category.StartsWith("Microsoft.AspNetCore.Routing", StringComparison.Ordinal)
            && entry.Text.Contains("/threads/search", StringComparison.Ordinal));
        Logs.Entries.Should().Contain(entry =>
            entry.Category == "Microsoft.EntityFrameworkCore.Database.Command"
            && entry.Text.Contains("websearch_to_tsquery", StringComparison.Ordinal));
        Logs.Entries.Should().NotContain(entry =>
            entry.Text.Contains(SecretPhrase, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
