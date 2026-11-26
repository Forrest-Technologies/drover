namespace Drover.Server.Tests;

/// <summary>
/// Bots as files over the API and real PostgreSQL: a bot is written down as its words alone, a file is read for
/// review, and it is imported as a paused bot with the tools every new bot starts with and nothing more.
/// </summary>
public sealed class BotFileApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Sample = """
        {
          "version": 1,
          "name": "Sample scribe",
          "description": "Takes notes.",
          "instructions": "Keep notes short.​",
          "suggestedModel": "sample-model"
        }
        """;

    private ScriptedModelProvider? model;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_preview_shows_the_bot_the_file_would_add_and_adds_nothing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var preview = await PostFor<BotImportPreview>(client, $"{Space(alice.SpaceId)}/bots/import/preview", new BotFileText(Sample), HttpStatusCode.OK);
        var bots = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");

        preview.Should().BeEquivalentTo(
            new BotImportPreview("Sample scribe", "Takes notes.", "Keep notes short.[U+200B]", 1),
            "a character that does not show is written out, and a space with no model names none");
        bots.Should().BeEmpty();
    }

    [Fact]
    public async Task An_imported_bot_is_paused_holds_only_what_a_new_bot_starts_with_and_is_turned_on_by_a_person()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var byHand = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("By hand", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);

        var preview = await PostFor<BotImportPreview>(client, $"{Space(alice.SpaceId)}/bots/import/preview", new BotFileText(Sample), HttpStatusCode.OK);
        var imported = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots/import", new BotFileText(Sample), HttpStatusCode.Created);
        var bot = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{imported.Id}/definition");
        var tools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{imported.Id}/tools");
        var byHandTools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{byHand.Id}/tools");
        var rules = await GetFor<List<BotRuleResponse>>(client, $"{Space(alice.SpaceId)}/bots/{imported.Id}/rules");
        (await client.Post($"{Space(alice.SpaceId)}/bots/{imported.Id}/resume", new { })).EnsureSuccessStatusCode();
        var turnedOn = await GetFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots/{imported.Id}");

        preview.Should().Match<BotImportPreview>(
            found => found.Model == "scripted" && found.ModelConnection == "Scripted",
            "a file cannot name a model the space does not have, so the connection's own is used");
        imported.Status.Should().Be("paused");
        bot.Should().Match<BotDetail>(found => found.Name == "Sample scribe" && found.Instructions == "Keep notes short.​"
            && found.DescriptionForBots == "Takes notes." && found.Model == "scripted" && found.Status == "paused" && found.ParentBotId == null);
        bot.SandboxPolicy.Should().Be("none", "a file never gives a bot a sandbox");
        tools.Select(entry => (entry.Name, entry.Grant)).Should().BeEquivalentTo(
            byHandTools.Select(entry => (entry.Name, entry.Grant)),
            "a file grants nothing: its bot starts with what any new bot starts with");
        rules.Should().BeEmpty();
        turnedOn.Status.Should().Be("active");
    }

    [Fact]
    public async Task With_no_model_in_the_space_nothing_is_imported_and_the_answer_says_what_is_missing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var import = await client.Post($"{Space(alice.SpaceId)}/bots/import", new BotFileText(Sample));

        import.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(import)).Should().Be("A bot needs a model, and this space has none it can use yet. Connect a model, then import this.");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("\"version\": 1,", "\"version\": 1, \"grants\": [\"everything\"],", "This is not a bot file Drover can read.")]
    [InlineData("\"version\": 1,", "\"version\": 1, \"sandboxPolicy\": \"own\",", "This is not a bot file Drover can read.")]
    [InlineData("\"version\": 1,", "\"version\": 2,", "This bot file is written in version 2 of the format. This server reads version 1.")]
    [InlineData("\"version\": 1,", "\"version\": 1, \"contents\": [],", "This is an extension file, not a bot file. Import it on the Extensions screen.")]
    public async Task A_file_that_is_refused_adds_nothing_and_says_why(string from, string to, string problem)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        await CreateConnection(client, alice.SpaceId);
        var changed = Sample.Replace(from, to, StringComparison.Ordinal);

        var preview = await client.Post($"{Space(alice.SpaceId)}/bots/import/preview", new BotFileText(changed));
        var import = await client.Post($"{Space(alice.SpaceId)}/bots/import", new BotFileText(changed));

        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(preview)).Should().StartWith(problem);
        import.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_bot_is_written_down_as_its_words_and_nothing_else_and_the_file_imports_elsewhere()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var connection = await CreateConnection(client, alice.SpaceId);
        var bobsConnection = await CreateConnection(asBob, bob.SpaceId);
        var bot = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Sample Scribe", "Keep notes short.", connection.Id, null), HttpStatusCode.Created);

        var exported = await client.Get($"{Space(alice.SpaceId)}/bots/{bot.Id}/export");
        var fields = (await exported.Content.ReadFromJsonAsync<JsonElement>(Token)).EnumerateObject().Select(field => field.Name).ToList();
        var file = await GetFor<BotFile>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/export");
        var imported = await PostFor<BotSummary>(asBob, $"{Space(bob.SpaceId)}/bots/import", new BotFileText(BotFiles.Write(file)), HttpStatusCode.Created);
        var bobsBot = await GetFor<BotDetail>(asBob, $"{Space(bob.SpaceId)}/bots/{imported.Id}/definition");

        fields.Should().BeEquivalentTo(
            ["version", "name", "instructions", "description", "suggestedModel"],
            "a file holds a bot's words: no grant, sandbox, limit, schedule, rule, or note has a field to be in");
        file.Should().BeEquivalentTo(new BotFile(1, "Sample Scribe", "Keep notes short.") { SuggestedModel = bot.Model });
        imported.Should().Match<BotSummary>(found => found.Name == "Sample Scribe" && found.Status == "paused");
        bobsBot.ModelConnectionId.Should().Be(bobsConnection.Id, "a bot is made on a connection of its own space, never another's");
    }

    [Fact]
    public async Task Only_an_admin_of_the_bots_own_space_writes_it_down_or_imports_a_file()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "carol", MembershipRole.Member);
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var asMember = NewClient();
        (await asMember.SignIn(member.Email, member.Password)).EnsureSuccessStatusCode();
        var connection = await CreateConnection(client, alice.SpaceId);
        var bot = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Sample scribe", "Keep notes short.", connection.Id, null), HttpStatusCode.Created);

        var byMember = await asMember.Get($"{Space(alice.SpaceId)}/bots/{bot.Id}/export");
        var previewByMember = await asMember.Post($"{Space(alice.SpaceId)}/bots/import/preview", new BotFileText(Sample));
        var importByMember = await asMember.Post($"{Space(alice.SpaceId)}/bots/import", new BotFileText(Sample));
        var fromBobsSpace = await asBob.Get($"{Space(bob.SpaceId)}/bots/{bot.Id}/export");
        var intoAlices = await asBob.Post($"{Space(alice.SpaceId)}/bots/import", new BotFileText(Sample));
        var unknown = await client.Get($"{Space(alice.SpaceId)}/bots/{Guid.CreateVersion7()}/export");

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        previewByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        importByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fromBobsSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "another space's bot is not there to be written down");
        intoAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().ContainSingle("nobody else added a bot");
    }

    [Fact]
    public async Task A_request_larger_than_a_bot_file_can_be_is_refused_before_it_is_read()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var body = JsonSerializer.Serialize(new BotFileText(new string(' ', 600 * 1024)), JsonSerializerOptions.Web);

        var response = await client.PostContent($"{Space(alice.SpaceId)}/bots/import/preview", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Title(response)).Should().Be("This file is larger than a bot file can be (256 KB).");
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private async Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId) =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"),
            HttpStatusCode.Created);

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

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    #endregion
}
