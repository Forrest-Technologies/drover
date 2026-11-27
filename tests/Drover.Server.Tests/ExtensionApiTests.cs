namespace Drover.Server.Tests;

/// <summary>
/// Extensions over the API and real PostgreSQL: a file is read for review and kept, what it offers (themes, rules)
/// reaches its own space alone, nothing is given to any bot, and removing it takes what it offered away.
/// </summary>
public sealed class ExtensionApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly JsonSerializerOptions written = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static readonly RuleTemplate sampleRule = new("Sample: ask before writing", "before_tool", "ask")
    {
        Conditions = [new RuleConditionBody("tool_kind") { ToolKinds = ["write"] }],
    };

    private ScriptedModelProvider? model;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_preview_shows_what_the_file_holds_and_adds_nothing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var preview = await PostFor<ExtensionPreview>(client, $"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample()), HttpStatusCode.OK);
        var installed = await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions");
        var themes = await GetFor<List<SpaceTheme>>(client, $"{Space(alice.SpaceId)}/themes");

        preview.Should().Match<ExtensionPreview>(found => found.PackageId == "sample.studio" && found.Name == "Sample studio"
            && found.Description == "A look and a rule." && found.Connection == null && !found.AlreadyInstalled);
        preview.Parts.Themes.Should().Equal(new ExtensionTheme("sample-dusk", "Sample dusk", preview.Parts.Themes[0].Intent));
        preview.Parts.Rules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new BotRuleBody("Sample: ask before writing", true, "before_tool", "ask", sampleRule.Conditions));
        installed.Should().BeEmpty();
        themes.Should().BeEmpty();
    }

    [Fact]
    public async Task Importing_keeps_the_file_offers_its_themes_to_members_and_gives_no_bot_anything()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "carol", MembershipRole.Member);
        var client = await SignIn(alice);
        var asMember = NewClient();
        (await asMember.SignIn(member.Email, member.Password)).EnsureSuccessStatusCode();
        var connection = await CreateConnection(client, alice.SpaceId);
        var bot = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("By hand", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);

        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);
        var listed = await GetFor<List<ExtensionSummary>>(asMember, $"{Space(alice.SpaceId)}/extensions");
        var themes = await GetFor<List<SpaceTheme>>(asMember, $"{Space(alice.SpaceId)}/themes");
        var bots = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");
        var rules = await GetFor<List<BotRuleResponse>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/rules");

        imported.Should().Match<ExtensionSummary>(found => found.PackageId == "sample.studio" && found.Name == "Sample studio" && found.Connection == null);
        listed.Should().ContainSingle("any member sees what is installed").Which.Should().BeEquivalentTo(imported, options => options.Excluding(found => found.InstalledAt));
        themes.Should().ContainSingle().Which.Should().Match<SpaceTheme>(theme => theme.Id == "sample-dusk" && theme.Name == "Sample dusk" && theme.Source == "Sample studio");
        ThemeGates.CheckFile(themes[0].File, "sample-dusk").Should().BeEmpty("the device is handed a theme file it can check and use");
        bots.Should().ContainSingle("an extension adds no bot");
        rules.Should().BeEmpty("an extension gives its rules to no bot: a person adds one");
    }

    [Fact]
    public async Task A_rule_from_an_extension_is_added_to_a_bot_the_way_a_rule_written_by_hand_is()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var bot = await PostFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("By hand", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        var added = await PostFor<BotRuleResponse>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/rules", imported.Parts.Rules[0], HttpStatusCode.Created);
        (await client.Delete($"{Space(alice.SpaceId)}/extensions/{imported.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var kept = await GetFor<List<BotRuleResponse>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/rules");

        added.Should().Match<BotRuleResponse>(rule => rule.Name == "Sample: ask before writing" && rule.Checkpoint == "before_tool" && rule.Effect == "ask" && rule.Enabled);
        kept.Should().ContainSingle("a rule a person added is the bot's own, and stays when the extension goes").Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public async Task An_extension_is_imported_once()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        var again = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()));
        var preview = await PostFor<ExtensionPreview>(client, $"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample()), HttpStatusCode.OK);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(again)).Should().Be("Sample studio is already in this space.");
        preview.AlreadyInstalled.Should().BeTrue();
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_theme_id_another_extension_already_offers_is_refused()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        var second = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample("sample.other")));

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(second)).Should().Be("This space already has a theme with the id \"sample-dusk\", from Sample studio. Nothing was added.");
        (await GetFor<List<SpaceTheme>>(client, $"{Space(alice.SpaceId)}/themes")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_file_that_is_refused_adds_nothing_and_says_why()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var withGrants = Sample().Replace("\"version\":2,", "\"version\":2,\"grants\":[\"everything\"],", StringComparison.Ordinal);
        var withBot = """{"version":1,"id":"sample.note-taker","name":"Sample","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."}]}""";
        var withSkill = """{"version":2,"id":"sample.skills","name":"Sample","contents":[{"kind":"skill","name":"Sample skill"}]}""";
        var unreadable = Write(new ExtensionPackage(2, "sample.fog", "Sample fog") { Contents = [new ThemePart(JsonElement.Parse(SampleThemes.Unreadable()))] });

        var preview = await client.Post($"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(withGrants));
        var import = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(withGrants));
        var bot = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(withBot));
        var skill = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(withSkill));
        var fog = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(unreadable));

        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(preview)).Should().StartWith("This is not an extension file Drover can read.");
        import.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(bot)).Should().StartWith("This extension holds a bot, and an extension no longer adds one.");
        (await Title(skill)).Should().Be("This extension holds a part of the kind \"skill\", which this server does not know. Nothing was added.");
        (await Title(fog)).Should().StartWith("The theme \"sample-fog\" in this extension does not pass the theme checks:");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().BeEmpty();
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().BeEmpty();
        (await GetFor<List<SpaceTheme>>(client, $"{Space(alice.SpaceId)}/themes")).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_an_admin_imports_and_removes_and_another_space_sees_and_reaches_nothing()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "carol", MembershipRole.Member);
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var asMember = NewClient();
        (await asMember.SignIn(member.Email, member.Password)).EnsureSuccessStatusCode();
        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        var previewByMember = await asMember.Post($"{Space(alice.SpaceId)}/extensions/preview", new ExtensionText(Sample()));
        var importByMember = await asMember.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()));
        var removeByMember = await asMember.Delete($"{Space(alice.SpaceId)}/extensions/{imported.Id}");
        var intoAlices = await asBob.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()));
        var alicesThemes = await asBob.Get($"{Space(alice.SpaceId)}/themes");
        var removeFromBobs = await asBob.Delete($"{Space(bob.SpaceId)}/extensions/{imported.Id}");
        var removeFromAlices = await asBob.Delete($"{Space(alice.SpaceId)}/extensions/{imported.Id}");
        var bobsPreview = await PostFor<ExtensionPreview>(asBob, $"{Space(bob.SpaceId)}/extensions/preview", new ExtensionText(Sample()), HttpStatusCode.OK);
        var bobsList = await GetFor<List<ExtensionSummary>>(asBob, $"{Space(bob.SpaceId)}/extensions");
        var bobsThemes = await GetFor<List<SpaceTheme>>(asBob, $"{Space(bob.SpaceId)}/themes");
        var bobsOwn = await PostFor<ExtensionSummary>(asBob, $"{Space(bob.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        previewByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        importByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        removeByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        intoAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        alicesThemes.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        removeFromBobs.StatusCode.Should().Be(HttpStatusCode.NotFound, "another space's extension is not there to be removed");
        removeFromAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        bobsPreview.AlreadyInstalled.Should().BeFalse("another space's extensions are not this space's");
        bobsList.Should().BeEmpty();
        bobsThemes.Should().BeEmpty("a theme is offered to the members of the space that imported it, and to nobody else");
        bobsOwn.PackageId.Should().Be("sample.studio", "two spaces may each hold the same extension, and the same theme id");
        (await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions")).Should().ContainSingle("nobody else removed it");
    }

    [Fact]
    public async Task Removing_an_extension_takes_its_themes_away_and_lets_it_be_imported_again()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var imported = await PostFor<ExtensionSummary>(client, $"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()), HttpStatusCode.Created);

        var removed = await client.Delete($"{Space(alice.SpaceId)}/extensions/{imported.Id}");
        var again = await client.Delete($"{Space(alice.SpaceId)}/extensions/{imported.Id}");
        var listed = await GetFor<List<ExtensionSummary>>(client, $"{Space(alice.SpaceId)}/extensions");
        var themes = await GetFor<List<SpaceTheme>>(client, $"{Space(alice.SpaceId)}/themes");
        var back = await client.Post($"{Space(alice.SpaceId)}/extensions", new ExtensionText(Sample()));

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        listed.Should().BeEmpty();
        themes.Should().BeEmpty();
        back.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private static string Write(ExtensionPackage package) => JsonSerializer.Serialize(package, written);

    /// <summary>A sample extension with one theme and one rule. Every value in it is a sample.</summary>
    private static string Sample(string id = "sample.studio") =>
        Write(new ExtensionPackage(2, id, "Sample studio")
        {
            Description = "A look and a rule.",
            Contents = [new ThemePart(JsonElement.Parse(SampleThemes.File())), sampleRule],
        });

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
