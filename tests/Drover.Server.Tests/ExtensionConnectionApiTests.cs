namespace Drover.Server.Tests;

/// <summary>
/// An extension that names a tool server, over the API and real PostgreSQL, with a real tool server on loopback:
/// importing contacts nothing, a person connects it from the file's own address with the extension's settings
/// filled in, a secret is never returned or kept with the extension, and no bot is given a tool.
/// </summary>
public sealed class ExtensionConnectionApiTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Private Fields

    private const string HeaderName = "X-Api-Key";

    private static readonly JsonSerializerOptions written = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    #endregion

    #region Tests

    [Fact]
    public async Task Importing_contacts_nothing_and_connecting_uses_the_files_address_and_lists_the_suggested_tools()
    {
        var secret = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var bot = await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = File(server.Url.ToString(), "header", HeaderName);

        var preview = await PostFor<ExtensionPreview>(client, $"{space}/extensions/preview", new ExtensionText(file));
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));
        var before = await Connections(client, space);
        var withoutYes = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(HeaderValue: secret));
        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: secret));
        var body = await connected.Content.ReadAsStringAsync(Token);
        var summary = JsonSerializer.Deserialize<ExtensionSummary>(body, JsonSerializerOptions.Web)!;
        var after = await Connections(client, space);
        var again = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: secret));
        var botTools = await Get<List<BotToolEntry>>(client, $"{space}/bots/{bot.Id}/tools");

        preview.Connection.Should().BeEquivalentTo(new ExtensionConnectionPreview("Helpdesk", server.Url.ToString(), server.Url.Host, "header", HeaderName, IsLocal: true, Available: true)
        {
            AsksForHeader = true,
            SuggestedTools = [new SuggestedTool("echo", "Repeats what it is given."), new SuggestedTool("absent", "Not a tool the server has.")],
        });
        imported.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId == null && found.Status == null);
        imported.Connection!.Tools.Should().OnlyContain(tool => tool.ToolId == null);
        before.Should().BeEmpty("importing makes no connection and asks the tool server nothing");
        withoutYes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(withoutYes)).Should().Be("This address is on this server's own network, or is not https. Tick the box to say you mean to connect to it.");
        connected.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(secret, "a header value is never returned");
        summary.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId != null && found.Status == "ready" && found.Problem == null);
        summary.Connection!.Tools.Select(tool => (tool.Name, Listed: tool.ToolId != null)).Should().Equal(
            [("echo", true), ("absent", false)],
            "a suggested tool is found once the server lists it");
        after.Should().ContainSingle().Which.Should().Match<ToolConnectionSummary>(connection =>
            connection.Id == summary.Connection.ConnectionId && connection.Url == server.Url.ToString() && connection.AuthKind == "header"
            && connection.Trust == "untrusted" && connection.Status == "ready");
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(again)).Should().Be("Helpdesk is already connected.");
        (await Connections(client, space)).Should().ContainSingle("a second press makes no second connection");
        botTools.Where(entry => entry.ConnectionId == summary.Connection.ConnectionId).Should().OnlyContain(
            entry => entry.Grant == "off",
            "connecting an extension's server gives no bot anything: a person grants a tool in the bot's settings");
    }

    [Fact]
    public async Task Settings_fill_in_the_address_and_the_header_and_a_secret_is_neither_kept_nor_returned()
    {
        var secret = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, $"Key {secret}"));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var address = server.Url.ToString();
        var template = address.Replace(server.Url.Authority, "{where}", StringComparison.Ordinal);
        var file = Write(new ExtensionPackage(2, "sample.helpdesk", "Sample helpdesk")
        {
            Settings =
            [
                new ExtensionSetting("where", "Server", ExtensionSetting.Text) { Help = "The host and port of the sample server." },
                new ExtensionSetting("token", "API key", ExtensionSetting.Secret),
            ],
            Contents = [new ConnectionRecipe("Helpdesk", template) { Auth = "header", HeaderName = HeaderName, HeaderTemplate = "Key {token}" }],
        });

        var preview = await PostFor<ExtensionPreview>(client, $"{space}/extensions/preview", new ExtensionText(file));
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));
        var unfilled = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));
        var elsewhere = await client.Post(
            $"{space}/extensions/{imported.Id}/connection",
            new ConnectExtensionRequest(AcceptLocalAddress: true) { Settings = new Dictionary<string, string> { ["where"] = "user:pass@tools.example.test", ["token"] = secret } });
        var connected = await client.Post(
            $"{space}/extensions/{imported.Id}/connection",
            new ConnectExtensionRequest(AcceptLocalAddress: true) { Settings = new Dictionary<string, string> { ["where"] = server.Url.Authority, ["token"] = secret } });
        var body = await connected.Content.ReadAsStringAsync(Token);
        var summary = JsonSerializer.Deserialize<ExtensionSummary>(body, JsonSerializerOptions.Web)!;
        var listed = await client.Get($"{space}/extensions");
        var listedBody = await listed.Content.ReadAsStringAsync(Token);

        preview.Connection.Should().Match<ExtensionConnectionPreview>(
            found => found.DependsOnSettings && found.Host == "{where}" && !found.IsLocal && !found.AsksForHeader,
            "where calls go is not known until the setting is filled in, and the review says so");
        preview.Parts.Settings.Select(setting => (setting.Key, setting.Type)).Should().Equal([("where", "text"), ("token", "secret")]);
        unfilled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(unfilled)).Should().Be("Fill in Server.");
        elsewhere.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(elsewhere)).Should().StartWith(
            "With these settings the address is not one Drover can connect to.",
            "a setting cannot make an address a person could not add by hand");
        connected.StatusCode.Should().Be(HttpStatusCode.OK, body);
        summary.Connection.Should().Match<ExtensionConnectionSummary>(
            found => found.Status == "ready" && found.Url == address && found.Host == server.Url.Host && !found.DependsOnSettings && found.IsLocal,
            "the server answered, so the header carried the secret, and the row now shows where calls go");
        summary.FilledSettings.Should().BeEquivalentTo(new Dictionary<string, string> { ["where"] = server.Url.Authority }, "a secret is never kept with the extension");
        body.Should().NotContain(secret);
        listedBody.Should().NotContain(secret);
    }

    [Fact]
    public async Task Removing_the_connection_keeps_the_extension_which_can_be_connected_again()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url.ToString(), "none", null)));
        var connected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        var removed = await client.Delete($"{space}/tool-connections/{connected.Connection!.ConnectionId}");
        var listed = (await Get<List<ExtensionSummary>>(client, $"{space}/extensions")).Single();
        var reconnected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        listed.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId == null && found.Status == null);
        reconnected.Connection!.ConnectionId.Should().NotBeNull();
        (reconnected.Connection.ConnectionId != connected.Connection.ConnectionId).Should().BeTrue("connecting again makes a new connection, not the removed one");
    }

    [Fact]
    public async Task Removing_the_extension_removes_the_connection_it_made_and_the_grants_on_its_tools()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var bot = await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url.ToString(), "none", null)));
        var connected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));
        var echo = connected.Connection!.Tools.Single(tool => tool.Name == "echo");
        (await client.Put($"{space}/bots/{bot.Id}/tools/{echo.ToolId}", new SetToolGrantRequest("ask"))).EnsureSuccessStatusCode();

        var removed = await client.Delete($"{space}/extensions/{imported.Id}");
        var botTools = await Get<List<BotToolEntry>>(client, $"{space}/bots/{bot.Id}/tools");

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Connections(client, space)).Should().BeEmpty("the connection the extension made goes with it");
        botTools.Should().NotContain(entry => entry.ConnectionId == connected.Connection.ConnectionId, "its tools and the grants on them go too");
        (await Get<List<ExtensionSummary>>(client, $"{space}/extensions")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_bot_using_an_extensions_tool_server_is_saved_as_a_file_with_no_address_credential_or_grant()
    {
        var secret = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var bot = await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url.ToString(), "header", HeaderName)));
        var connected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: secret));
        var echo = connected.Connection!.Tools.Single(tool => tool.Name == "echo");
        (await client.Put($"{space}/bots/{bot.Id}/tools/{echo.ToolId}", new SetToolGrantRequest("ask"))).EnsureSuccessStatusCode();

        var exported = await client.Get($"{space}/bots/{bot.Id}/export");
        var body = await exported.Content.ReadAsStringAsync(Token);

        exported.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(secret, "a header value is never written to a file");
        body.Should().NotContain(server.Url.Authority, "a bot file names no tool server");
        body.Should().NotContain(HeaderName, "a bot file names no header");
        body.Should().NotContain("echo", "the tools a bot was granted are not in its file");
        body.Should().NotContain(imported.Id.ToString(), "the extension is not part of the file");
    }

    [Fact]
    public async Task Only_an_admin_of_the_extensions_own_space_connects_it()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "carol", MembershipRole.Member);
        var client = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        await AllowPrivateAddresses(bob.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url.ToString(), "none", null)));
        var yes = new ConnectExtensionRequest(AcceptLocalAddress: true);

        var byMember = await asMember.Post($"{space}/extensions/{imported.Id}/connection", yes);
        var fromBobsSpace = await asBob.Post($"/api/spaces/{bob.SpaceId}/extensions/{imported.Id}/connection", yes);
        var intoAlices = await asBob.Post($"{space}/extensions/{imported.Id}/connection", yes);
        var unknown = await client.Post($"{space}/extensions/{Guid.CreateVersion7()}/connection", yes);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fromBobsSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "another space's extension is not there to be connected");
        intoAlices.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Connections(client, space)).Should().BeEmpty();
        (await Connections(asBob, $"/api/spaces/{bob.SpaceId}")).Should().BeEmpty();
    }

    [Fact]
    public async Task An_extension_with_no_tool_server_has_none_to_connect()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = Write(new ExtensionPackage(2, "sample.look", "Sample look") { Contents = [new ThemePart(JsonElement.Parse(SampleThemes.File()))] });
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));

        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        imported.Connection.Should().BeNull();
        connected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(connected)).Should().Be("This extension names no tool server.");
    }

    #endregion

    #region Helpers

    internal static string Write(ExtensionPackage package) => JsonSerializer.Serialize(package, written);

    private static string File(string address, string auth, string? headerName) =>
        Write(new ExtensionPackage(2, "sample.helpdesk", "Sample helpdesk")
        {
            Contents =
            [
                new ConnectionRecipe("Helpdesk", address)
                {
                    Auth = auth,
                    HeaderName = headerName,
                    SuggestedTools = [new SuggestedTool("echo", "Repeats what it is given."), new SuggestedTool("absent", "Not a tool the server has.")],
                },
            ],
        });

    private static Task<List<ToolConnectionSummary>> Connections(ApiClient client, string space) =>
        Get<List<ToolConnectionSummary>>(client, $"{space}/tool-connections");

    private static async Task<T> Get<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    #endregion
}

/// <summary>Where an operator has tool connections turned off, an extension still imports and its tool server does not connect.</summary>
public sealed class ExtensionConnectionSwitchTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:AllowToolConnections"] = "false",
    };

    #endregion

    #region Tests

    [Fact]
    public async Task With_tool_connections_off_the_extension_imports_and_the_tool_server_is_said_to_be_unavailable_and_is_not_connected()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = """{"version":2,"id":"sample.helpdesk","name":"Sample helpdesk","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""";

        var preview = await PostFor<ExtensionPreview>(client, $"{space}/extensions/preview", new ExtensionText(file));
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));
        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest());
        var refusal = await connected.Content.ReadAsStringAsync(Token);

        preview.Connection!.Available.Should().BeFalse();
        imported.Connection!.Available.Should().BeFalse();
        connected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refusal.Should().Contain("AllowToolConnections", "the message names the rule an operator would change");
    }

    #endregion
}
