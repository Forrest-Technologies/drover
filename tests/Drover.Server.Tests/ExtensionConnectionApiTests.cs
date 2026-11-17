namespace Drover.Server.Tests;

/// <summary>
/// An extension that names a tool server, over the API and real PostgreSQL, with a real tool server on loopback:
/// importing contacts nothing, a person connects it from the file's own address, and the bot gets only the tools a
/// person gives it.
/// </summary>
public sealed class ExtensionConnectionApiTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Private Fields

    private const string HeaderName = "X-Api-Key";

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
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = File(server.Url, "header", HeaderName);

        var preview = await PostFor<ExtensionPreview>(client, $"{space}/extensions/preview", new ExtensionText(file));
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));
        var before = await Connections(client, space);
        var withoutYes = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(HeaderValue: secret));
        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: secret));
        var body = await connected.Content.ReadAsStringAsync(Token);
        var summary = JsonSerializer.Deserialize<ExtensionSummary>(body, JsonSerializerOptions.Web)!;
        var after = await Connections(client, space);
        var again = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: secret));

        preview.Connection.Should().BeEquivalentTo(new ExtensionConnectionPreview("Helpdesk", server.Url.ToString(), server.Url.Host, "header", HeaderName, IsLocal: true, Available: true)
        {
            SuggestedTools = [new SuggestedTool("echo", "Repeats what it is given."), new SuggestedTool("absent", "Not a tool the server has.")],
        });
        imported.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId == null && found.Status == null);
        imported.Connection!.Tools.Should().OnlyContain(tool => tool.ToolId == null && tool.Grant == "off");
        before.Should().BeEmpty("importing makes no connection and asks the tool server nothing");
        withoutYes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(withoutYes)).Should().Be("This address is on this server's own network, or is not https. Tick the box to say you mean to connect to it.");
        connected.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(secret, "a header value is never returned");
        summary.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId != null && found.Status == "ready" && found.Problem == null);
        summary.Connection!.Tools.Select(tool => (tool.Name, Listed: tool.ToolId != null, tool.Grant)).Should().Equal(
            [("echo", true, "off"), ("absent", false, "off")],
            "a suggested tool is found once the server lists it, and none is granted for the person");
        after.Should().ContainSingle().Which.Should().Match<ToolConnectionSummary>(connection =>
            connection.Id == summary.Connection.ConnectionId && connection.Url == server.Url.ToString() && connection.AuthKind == "header"
            && connection.Trust == "untrusted" && connection.Status == "ready");
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(again)).Should().Be("Helpdesk is already connected.");
        (await Connections(client, space)).Should().ContainSingle("a second press makes no second connection");
    }

    [Fact]
    public async Task The_bot_gets_a_suggested_tool_only_when_a_person_grants_it_and_the_row_then_says_so()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url, "none", null)));
        var connected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));
        var echo = connected.Connection!.Tools.Single(tool => tool.Name == "echo");

        var botTools = await Get<List<BotToolEntry>>(client, $"{space}/bots/{imported.BotId}/tools");
        (await client.Put($"{space}/bots/{imported.BotId}/tools/{echo.ToolId}", new SetToolGrantRequest("ask"))).EnsureSuccessStatusCode();
        var listed = (await Get<List<ExtensionSummary>>(client, $"{space}/extensions")).Single();

        botTools.Where(entry => entry.ConnectionId == connected.Connection.ConnectionId).Should().OnlyContain(
            entry => entry.Grant == "off",
            "connecting an extension's server gives its bot nothing");
        listed.Connection!.Tools.Single(tool => tool.Name == "echo").Grant.Should().Be("ask");
        listed.BotStatus.Should().Be("paused", "granting a tool does not turn the bot on");
    }

    [Fact]
    public async Task Removing_the_connection_keeps_the_extension_which_can_be_connected_again()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url, "none", null)));
        var connected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        var removed = await client.Delete($"{space}/tool-connections/{connected.Connection!.ConnectionId}");
        var listed = (await Get<List<ExtensionSummary>>(client, $"{space}/extensions")).Single();
        var reconnected = await PostFor<ExtensionSummary>(client, $"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        listed.Connection.Should().Match<ExtensionConnectionSummary>(found => found.ConnectionId == null && found.Status == null);
        listed.BotName.Should().Be("Sample scribe", "the bot is the space's own and stays");
        reconnected.Connection!.ConnectionId.Should().NotBeNull();
        (reconnected.Connection.ConnectionId != connected.Connection.ConnectionId).Should().BeTrue("connecting again makes a new connection, not the removed one");
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
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(File(server.Url, "none", null)));
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
    public async Task A_bot_only_extension_has_no_tool_server_to_connect()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = """{"version":1,"id":"sample.bot-only","name":"Sample","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."}]}""";
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));

        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest(AcceptLocalAddress: true));

        imported.Connection.Should().BeNull();
        connected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(connected)).Should().Be("This extension names no tool server.");
    }

    #endregion

    #region Helpers

    private static string File(Uri address, string auth, string? headerName) =>
        ExtensionFile.Write(new ExtensionPackage(1, "sample.helpdesk", "Sample helpdesk")
        {
            Contents =
            [
                new BotTemplate("Sample scribe", "Keep notes short."),
                new ConnectionRecipe("Helpdesk", address.ToString())
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

/// <summary>Where an operator has tool connections turned off, an extension's bot still imports and its tool server does not connect.</summary>
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
    public async Task With_tool_connections_off_the_bot_imports_and_the_tool_server_is_said_to_be_unavailable_and_is_not_connected()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await CreateBot(client, alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var file = """{"version":1,"id":"sample.helpdesk","name":"Sample helpdesk","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."},{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""";

        var preview = await PostFor<ExtensionPreview>(client, $"{space}/extensions/preview", new ExtensionText(file));
        var imported = await PostFor<ExtensionSummary>(client, $"{space}/extensions", new ExtensionText(file));
        var connected = await client.Post($"{space}/extensions/{imported.Id}/connection", new ConnectExtensionRequest());
        var refusal = await connected.Content.ReadAsStringAsync(Token);

        preview.Connection!.Available.Should().BeFalse();
        imported.Should().Match<ExtensionSummary>(found => found.BotName == "Sample scribe" && found.BotStatus == "paused");
        imported.Connection!.Available.Should().BeFalse();
        connected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refusal.Should().Contain("AllowToolConnections", "the message names the rule an operator would change");
    }

    #endregion
}
