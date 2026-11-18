namespace Drover.Server.Tests;

/// <summary>
/// Extensions over the API and real PostgreSQL: a file is read for review, imported as a paused bot with the tools
/// every new bot starts with and nothing more, and seen by its own space alone.
/// </summary>
public sealed class ExtensionApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Sample = """
        {
          "version": 1,
          "id": "sample.note-taker",
          "name": "Sample note taker",
          "description": "Keeps notes.",
          "contents": [
            { "kind": "bot", "name": "Sample scribe", "instructions": "Keep notes short.​", "description": "Takes notes.", "suggestedModel": "sample-model" }
          ]
        }
        """;

    private ScriptedModelProvider? model;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_preview_shows_what_the_file_would_add_and_adds_nothing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var preview = await PostFor<ExtensionPreview>(client, $"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample), HttpStatusCode.OK);
        var installed = await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions");
        var bots = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");

        preview.Should().BeEquivalentTo(new ExtensionPreview("sample.note-taker", "Sample note taker", "Keeps notes.")
        {
            Bot = new ExtensionBotPreview("Sample scribe", "Takes notes.", "Keep notes short.[U+200B]", 1),
        }, "a space with no model names none");
        installed.Should().BeEmpty();
        bots.Should().BeEmpty();
    }

    [Fact]
    public async Task An_imported_bot_is_paused_holds_only_what_a_new_bot_starts_with_and_is_turned_on_by_a_person()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var byHand = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("By hand", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);

        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample), HttpStatusCode.Created);
        var bot = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{imported.BotId}/definition");
        var tools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{imported.BotId}/tools");
        var byHandTools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{byHand.Id}/tools");
        var listed = await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions");
        (await client.Post($"{Space(alice.SpaceId)}/bots/{imported.BotId}/resume", new { })).EnsureSuccessStatusCode();
        var turnedOn = await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions");

        imported.Should().Match<ExtensionSummary>(found => found.PackageId == "sample.note-taker" && found.Name == "Sample note taker"
            && found.BotName == "Sample scribe" && found.BotStatus == "paused");
        bot.Should().Match<BotDetail>(found => found.Name == "Sample scribe" && found.Instructions == "Keep notes short.​"
            && found.DescriptionForBots == "Takes notes." && found.Model == "scripted" && found.Status == "paused" && found.ParentBotId == null);
        bot.SandboxPolicy.Should().Be("none", "a file never gives a bot a sandbox");
        tools.Select(entry => (entry.Name, entry.Grant)).Should().BeEquivalentTo(
            byHandTools.Select(entry => (entry.Name, entry.Grant)),
            "an extension grants nothing: its bot starts with what any new bot starts with");
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(imported, options => options.Excluding(found => found.InstalledAt));
        listed[0].InstalledAt.Should().BeCloseTo(imported.InstalledAt, TimeSpan.FromMilliseconds(1));
        turnedOn.Single().BotStatus.Should().Be("active");
    }

    [Fact]
    public async Task The_suggested_model_is_used_only_where_the_connection_lists_it()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);

        var preview = await PostFor<ExtensionPreview>(client, $"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample), HttpStatusCode.OK);
        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample), HttpStatusCode.Created);
        var bot = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{imported.BotId}/definition");

        preview.Bot.Should().Match<ExtensionBotPreview>(
            found => found.Model == "scripted" && found.ModelConnection == "Scripted",
            "a file cannot name a model the space does not have, so the connection's own is used");
        bot.Model.Should().Be("scripted", "the bot is made on the model the review named");
        bot.ModelConnectionId.Should().Be(connection.Id);
    }

    [Fact]
    public async Task With_no_model_in_the_space_nothing_is_imported_and_the_answer_says_what_is_missing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var import = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample));

        import.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(import)).Should().Be("A bot needs a model to think with, and this space has none it can use yet. Connect a model, then import this.");
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().BeEmpty();
    }

    [Fact]
    public async Task An_extension_is_imported_once_and_a_second_import_adds_no_second_bot()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample), HttpStatusCode.Created);

        var again = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample));
        var preview = await PostFor<ExtensionPreview>(client, $"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample), HttpStatusCode.OK);
        var bots = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(again)).Should().Be("Sample note taker is already in this space.");
        preview.AlreadyInstalled.Should().BeTrue();
        bots.Should().ContainSingle();
    }

    [Fact]
    public async Task A_file_that_is_refused_adds_nothing_and_says_why()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var withGrants = Sample.Replace("\"version\": 1,", "\"version\": 1, \"grants\": [\"everything\"],", StringComparison.Ordinal);

        var preview = await client.Post($"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(withGrants));
        var import = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(withGrants));

        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(preview)).Should().StartWith("This is not an extension file Drover can read.");
        import.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().BeEmpty();
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_an_admin_imports_and_another_space_sees_and_reaches_nothing()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "carol", MembershipRole.Member);
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var asMember = NewClient();
        (await asMember.SignIn(member.Email, member.Password)).EnsureSuccessStatusCode();
        var connection = await CreateConnection(client, alice.SpaceId);
        var bobsConnection = await CreateConnection(asBob, bob.SpaceId);
        await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample), HttpStatusCode.Created);

        var previewByMember = await asMember.Post($"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample));
        var importByMember = await asMember.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample));
        var seenByMember = await GetFor<List<ExtensionSummary>>(asMember, $"{Space(alice.SpaceId)}/extensions");
        var intoAlices = await asBob.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample));
        var bobsPreview = await PostFor<ExtensionPreview>(asBob, $"{Space(bob.SpaceId)}/extensions/preview", new ExtensionText(Sample), HttpStatusCode.OK);
        var bobsList = await GetFor<List<ExtensionSummary>>(asBob, $"{Space(bob.SpaceId)}/extensions");
        var bobsOwn = await PostFor<ExtensionSummary>(asBob, $"{Space(bob.SpaceId)}/extensions", new ExtensionText(Sample), HttpStatusCode.Created);
        var bobsBot = await GetFor<BotDetail>(asBob, $"{Space(bob.SpaceId)}/bots/{bobsOwn.BotId}/definition");

        previewByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        importByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        seenByMember.Should().ContainSingle("any member sees what is installed");
        intoAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        bobsPreview.AlreadyInstalled.Should().BeFalse("another space's extensions are not this space's");
        bobsList.Should().BeEmpty();
        bobsOwn.PackageId.Should().Be("sample.note-taker", "two spaces may each hold the same extension");
        bobsBot.ModelConnectionId.Should().Be(bobsConnection.Id, "a bot is made on a connection of its own space, never another's");
    }

    [Fact]
    public async Task A_bot_is_written_down_as_its_words_and_nothing_else_and_the_file_imports_elsewhere()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var connection = await CreateConnection(client, alice.SpaceId);
        await CreateConnection(asBob, bob.SpaceId);
        var bot = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Sample Scribe", "Keep notes short.", connection.Id, null), HttpStatusCode.Created);

        var exported = await client.Get($"{Space(alice.SpaceId)}/extensions/from-bot/{bot.Id}");
        var fields = (await exported.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("contents")[0].EnumerateObject().Select(field => field.Name).ToList();
        var package = await GetFor<ExtensionPackage>(client, $"{Space(alice.SpaceId)}/extensions/from-bot/{bot.Id}");
        var imported = await PostFor<ExtensionSummary>(asBob, $"{Space(bob.SpaceId)}/extensions", new ExtensionText(ExtensionFile.Write(package)), HttpStatusCode.Created);

        fields.Should().BeEquivalentTo(
            ["kind", "name", "instructions", "description", "suggestedModel"],
            "a file holds a bot's words: no grant, sandbox, limit, schedule, or note has a field to be in");
        package.Should().BeEquivalentTo(new ExtensionPackage(1, bot.Handle, "Sample Scribe")
        {
            Contents = [new BotTemplate("Sample Scribe", "Keep notes short.") { SuggestedModel = bot.Model }],
        });
        imported.Should().Match<ExtensionSummary>(found => found.BotName == "Sample Scribe" && found.BotStatus == "paused");
    }

    [Fact]
    public async Task Only_an_admin_of_the_bots_own_space_writes_it_down_or_has_a_file_read()
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

        var byMember = await asMember.Get($"{Space(alice.SpaceId)}/extensions/from-bot/{bot.Id}");
        var readByMember = await asMember.Post($"{Space(alice.SpaceId)}/extensions/read", new ExtensionText(Sample));
        var fromBobsSpace = await asBob.Get($"{Space(bob.SpaceId)}/extensions/from-bot/{bot.Id}");
        var intoAlices = await asBob.Get($"{Space(alice.SpaceId)}/extensions/from-bot/{bot.Id}");
        var unknown = await client.Get($"{Space(alice.SpaceId)}/extensions/from-bot/{Guid.CreateVersion7()}");

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        readByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fromBobsSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "another space's bot is not there to be written down");
        intoAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_a_file_gives_its_package_as_the_importer_would_read_it_or_the_reason_it_would_not()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var read = await PostFor<ExtensionPackage>(client, $"{Space(alice.SpaceId)}/extensions/read", new ExtensionText(Sample), HttpStatusCode.OK);
        var refused = await client.Post($"{Space(alice.SpaceId)}/extensions/read", new ExtensionText(Sample.Replace("\"kind\": \"bot\"", "\"kind\": \"skill\"", StringComparison.Ordinal)));

        read.Contents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BotTemplate("Sample scribe", "Keep notes short.\u200B") { Description = "Takes notes.", SuggestedModel = "sample-model" },
            "the text is given back as it is, so the form holds what the file holds");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(refused)).Should().Be("This extension holds a part of the kind \"skill\", which this server does not know. Nothing was added.");
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_larger_than_an_extension_can_be_is_refused_before_it_is_read()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var body = JsonSerializer.Serialize(new ExtensionText(new string(' ', 600 * 1024)), JsonSerializerOptions.Web);

        var response = await client.PostContent($"{Space(alice.SpaceId)}/extensions/preview", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Title(response)).Should().Be("This file is larger than an extension can be (256 KB).");
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
