namespace Drover.Server.Tests;

/// <summary>
/// Tool connections through the API, end to end: the real adapter, a real tool server on loopback, the real worker
/// and agent runtime, and the scripted model in place of a provider.
/// </summary>
public sealed class ToolConnectionApiTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Private Fields

    private const string HeaderName = "X-Api-Key";

    #endregion

    #region Tests

    [Theory]
    [InlineData(ServerGeneration.Stateless, "stateless")]
    [InlineData(ServerGeneration.SessionBased, "session-based")]
    public async Task An_admin_connects_a_server_with_a_header_and_a_bot_uses_a_granted_tool_with_the_secret_nowhere_but_the_server(
        ServerGeneration generation,
        string badge)
    {
        Script = new ScriptedModel([new ToolCallOutput("c1", "helpdesk__echo", """{"text":"hello"}""")], [new TextOutput("It said hello.")]);
        var secret = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(generation, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var client = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var request = new CreateToolConnectionRequest("Helpdesk", server.Url.ToString(), "header", HeaderName, secret, "trusted");

        var byMember = await asMember.Post($"{space}/tool-connections", request);
        var created = await client.Post($"{space}/tool-connections", request);
        var createdBody = await created.Content.ReadAsStringAsync(Token);
        var listed = JsonSerializer.Deserialize<ToolConnectionRefresh>(createdBody, JsonSerializerOptions.Web)!;
        var seenByMember = await asMember.Get($"{space}/tool-connections");
        var toolsBody = await (await asMember.Get($"{space}/tool-connections/{listed.Connection.Id}/tools")).Content.ReadAsStringAsync(Token);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only an admin adds a connection");
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
        listed.Problem.Should().BeNull();
        listed.Connection.Should().Match<ToolConnectionSummary>(connection =>
            connection.Slug == "helpdesk" && connection.Status == "ready" && connection.AuthKind == "header" && connection.Trust == "trusted"
            && connection.NegotiatedProtocol!.StartsWith(badge));
        listed.Tools.Select(tool => tool.ModelName).Should().BeEquivalentTo(["helpdesk__echo", "helpdesk__record", "helpdesk__fail", "helpdesk__slow", "helpdesk__flood"]);
        listed.Tools.Should().OnlyContain(tool => tool.Sensitivity == "write", "a server's claim that a tool only reads decides nothing");
        listed.Added.Should().Be(5);
        seenByMember.StatusCode.Should().Be(HttpStatusCode.OK, "any member may see which connections exist");

        var bot = await CreateBot(client, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");
        var thread = await PostFor<ThreadSummary>(client, $"{space}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var sent = await PostFor<SendMessageResponse>(client, $"{space}/threads/{thread.Id}/messages", new SendMessageRequest("Say hello"));
        var run = await WaitForRun(client, alice.SpaceId, sent.Run.Id);
        var messages = await (await client.Get($"{space}/threads/{thread.Id}/messages")).Content.ReadAsStringAsync(Token);
        var events = await (await client.Get($"{space}/runs/{sent.Run.Id}/events")).Content.ReadAsStringAsync(Token);

        run.Status.Should().Be("completed", run.Error?.Message);
        Model!.ToolsOffered[0].Should().Contain("helpdesk__echo").And.NotContain("helpdesk__record", "a tool nobody granted is not sent to the model");
        Script.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"echo: hello\""));
        var everythingShown = string.Join('\n', createdBody, toolsBody, messages, events, await seenByMember.Content.ReadAsStringAsync(Token), string.Join('\n', Logs.Entries.Select(entry => entry.ToString())));
        everythingShown.Should().NotContain(secret, "a header value is never in an API response, an event, or a log");
    }

    [Fact]
    public async Task A_tool_server_that_does_not_answer_is_reported_on_the_connection_and_logged_as_a_warning_not_an_error()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var request = new CreateToolConnectionRequest("Nobody home", $"http://127.0.0.1:{ClosedPort()}/mcp", "none", null, null, "trusted");

        var created = await client.Post($"/api/spaces/{alice.SpaceId}/tool-connections", request);
        var listed = JsonSerializer.Deserialize<ToolConnectionRefresh>(await created.Content.ReadAsStringAsync(Token), JsonSerializerOptions.Web)!;

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        listed.Connection.Status.Should().Be("failed");
        listed.Problem.Should().NotBeNullOrWhiteSpace();
        Logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error,
            "a tool server that is down is the server's own state, recorded on the connection; it is not a failure of Drover");
    }

    [Fact]
    public async Task A_refused_credential_is_reported_in_plain_words_and_a_new_header_fixes_it()
    {
        var secret = NewPassword();
        var wrong = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, secret));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";

        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Helpdesk", server.Url.ToString(), "header", HeaderName, wrong));
        var replaced = await client.Put($"{space}/tool-connections/{created.Connection.Id}/header", new ReplaceToolHeaderRequest(HeaderName, secret));
        var refreshed = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections/{created.Connection.Id}/refresh", new { });

        created.Problem.Should().Contain("refused the credential").And.NotContain(wrong);
        created.Connection.Status.Should().Be("failed");
        created.Connection.LastError.Should().NotContain(wrong);
        created.Tools.Should().BeEmpty();
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replaced.Content.ReadAsStringAsync(Token)).Should().NotContain(secret);
        refreshed.Problem.Should().BeNull();
        refreshed.Connection.Status.Should().Be("ready");
        refreshed.Tools.Should().HaveCount(5);
    }

    [Fact]
    public async Task A_connection_is_validated_removed_by_an_admin_only_and_invisible_from_another_space()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var outsider = await SeedPerson("outsider");
        var client = await SignIn(alice.Email, alice.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";

        var noAddress = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", "not a url"));
        var withPassword = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", "https://user:pass@tools.example.test/"));
        var reservedHeader = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", server.Url.ToString(), "header", "Host", "evil.example.test"));
        var signIn = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Later", server.Url.ToString(), "oauth"));
        var unknownSetting = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", server.Url.ToString(), Trust: "very"));
        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Notes", server.Url.ToString()));
        var id = created.Connection.Id;
        var fromOtherSpace = await asOutsider.Get($"{space}/tool-connections/{id}/tools");
        var inOwnSpace = await asOutsider.Get($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}/tools");
        var removedFromOwnSpace = await asOutsider.Delete($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}");
        var updated = await client.Put($"{space}/tool-connections/{id}", new UpdateToolConnectionRequest("trusted", "stateless_only"));
        var removed = await client.Delete($"{space}/tool-connections/{id}");
        var afterwards = await client.Get($"{space}/tool-connections/{id}/tools");

        noAddress.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a password in the address would be stored and shown in the clear");
        reservedHeader.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a connection may not set a header that belongs to the transport");
        signIn.StatusCode.Should().Be(HttpStatusCode.BadRequest, "sign-in to a tool server is not built yet, and says so");
        unknownSetting.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        created.Connection.Trust.Should().Be("untrusted", "a connection is not trusted until a person says so");
        fromOtherSpace.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        inOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "a connection id from another space finds nothing in one's own");
        removedFromOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await updated.Content.ReadFromJsonAsync<ToolConnectionSummary>(Token))!.Should().Match<ToolConnectionSummary>(connection =>
            connection.Trust == "trusted" && connection.ProtocolMode == "stateless_only" && connection.NegotiatedProtocol == null);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterwards.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_changed_tool_shows_what_was_last_reviewed_and_only_an_admin_of_the_space_confirms_it()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var client = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Notes", server.Url.ToString()));
        var id = created.Connection.Id;
        var echo = created.Tools.Single(tool => tool.Name == "echo");
        var bot = await CreateBot(client, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");
        await PretendTheToolWasReviewedAs(alice.SpaceId, echo.Id, "Repeats text.");

        var relisted = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections/{id}/refresh", new { });
        var waiting = relisted.Tools.Single(tool => tool.Name == "echo");
        var byMember = await asMember.Post($"{space}/tool-connections/{id}/tools/{echo.Id}/review", new { });
        var byOutsider = await asOutsider.Post($"{space}/tool-connections/{id}/tools/{echo.Id}/review", new { });
        var fromOwnSpace = await asOutsider.Post($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}/tools/{echo.Id}/review", new { });
        var stillWaiting = (await (await client.Get($"{space}/tool-connections/{id}/tools")).Content.ReadFromJsonAsync<List<ConnectionToolSummary>>(Token))!
            .Single(tool => tool.Name == "echo");
        var confirmed = await PostFor<ConnectionToolSummary>(client, $"{space}/tool-connections/{id}/tools/{echo.Id}/review", new { });
        var unknownTool = await client.Post($"{space}/tool-connections/{id}/tools/{Guid.CreateVersion7()}/review", new { });

        relisted.Changed.Should().Be(1);
        relisted.GrantsForReview.Should().Be(1);
        waiting.NeedsReview.Should().BeTrue();
        waiting.Description.Should().Be("Repeats the text it is given.");
        waiting.InputSchema.Should().Contain("\"text\"");
        waiting.Reviewed.Should().NotBeNull();
        waiting.Reviewed!.Description.Should().Be("Repeats text.", "the definition a person last saw is kept beside the new one");
        relisted.Tools.Where(tool => tool.Name != "echo").Should().OnlyContain(tool => !tool.NeedsReview && tool.Reviewed == null);
        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only an admin accepts a changed definition");
        byOutsider.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        fromOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        stillWaiting.NeedsReview.Should().BeTrue("nobody who may confirm has confirmed");
        confirmed.NeedsReview.Should().BeFalse();
        confirmed.Reviewed.Should().BeNull();
        unknownTool.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task No_slug_holds_two_underscores_in_a_row_so_no_two_connections_give_a_tool_the_same_name()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";

        // Twenty-three letters and then a space: cut to length, the stem ends where the space was.
        var name = $"{new string('a', 23)} helpdesk";
        List<string> slugs = [];

        foreach (var each in new[] { name, name, name, "a__b", "__", "tools_", "Ünïcödé Sërvër" })
        {
            var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest(each, "https://tools.example.test/mcp"));
            slugs.Add(created.Connection.Slug);
        }

        slugs.Should().OnlyHaveUniqueItems();
        slugs.Should().AllSatisfy(slug =>
        {
            slug.Should().NotContain("__", "the two underscores separate a connection from its tool in the name a model sees");
            slug.Should().NotStartWith("_").And.NotEndWith("_");
            slug.Should().MatchRegex("^[a-z0-9_]{1,32}$");
        });
    }

    [Fact]
    public async Task A_setting_left_out_of_a_change_stays_as_it_was()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";
        var created = await PostFor<ToolConnectionRefresh>(
            client,
            $"{space}/tool-connections",
            new CreateToolConnectionRequest("Notes", "https://tools.example.test/mcp", Trust: "trusted", ProtocolMode: "session_only"));
        var path = $"{space}/tool-connections/{created.Connection.Id}";

        var nothing = await (await client.Put(path, new UpdateToolConnectionRequest(null, null))).Content.ReadFromJsonAsync<ToolConnectionSummary>(Token);
        var trustOnly = await (await client.Put(path, new UpdateToolConnectionRequest("untrusted", null))).Content.ReadFromJsonAsync<ToolConnectionSummary>(Token);
        var modeOnly = await (await client.Put(path, new UpdateToolConnectionRequest(null, "stateless_only"))).Content.ReadFromJsonAsync<ToolConnectionSummary>(Token);

        nothing.Should().Match<ToolConnectionSummary>(connection => connection.Trust == "trusted" && connection.ProtocolMode == "session_only");
        trustOnly.Should().Match<ToolConnectionSummary>(connection => connection.Trust == "untrusted" && connection.ProtocolMode == "session_only");
        modeOnly.Should().Match<ToolConnectionSummary>(connection => connection.Trust == "untrusted" && connection.ProtocolMode == "stateless_only");
    }

    [Theory]
    [InlineData("Proxy-Authorization")]
    [InlineData("Origin")]
    [InlineData("User-Agent")]
    [InlineData("Upgrade")]
    [InlineData("TE")]
    [InlineData("Expect")]
    [InlineData("Last-Event-ID")]
    [InlineData("X-Forwarded-For")]
    [InlineData("x-forwarded-host")]
    [InlineData("Host")]
    public async Task A_header_that_belongs_to_the_transport_or_disguises_the_request_is_not_a_credential(string header)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";

        var created = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Bad", "https://tools.example.test/mcp", "header", header, NewPassword()));

        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_address_with_a_key_in_it_is_refused_and_the_key_is_stored_and_shown_nowhere()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var space = $"/api/spaces/{alice.SpaceId}";
        var key = NewPassword();

        var withQuery = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Keyed", $"https://tools.example.test/mcp?api_key={key}"));
        var withFragment = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Keyed", $"https://tools.example.test/mcp#{key}"));
        var bareQuestionMark = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Keyed", "https://tools.example.test/mcp?"));
        var refusal = await withQuery.Content.ReadAsStringAsync(Token);
        var listed = await (await client.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token);

        withQuery.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the address is stored and shown to every member, and so would the key be");
        withFragment.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        bareQuestionMark.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refusal.Should().Contain("header", "the message says where a key belongs");
        string.Join('\n', refusal, await withFragment.Content.ReadAsStringAsync(Token), listed, string.Join('\n', Logs.Entries.Select(entry => entry.ToString())))
            .Should().NotContain(key, "the key is not repeated in the answer, stored, or logged");
        listed.Should().NotContain("Keyed", "nothing was saved");
    }

    [Fact]
    public async Task A_replaced_header_is_the_one_sent_on_the_very_next_call_to_a_server_that_keeps_sessions()
    {
        var old = NewPassword();
        var replacement = NewPassword();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.SessionBased, HeaderName, old));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var created = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections", new CreateToolConnectionRequest("Helpdesk", server.Url.ToString(), "header", HeaderName, old));
        var id = created.Connection.Id;
        var sessionsBefore = server.Handshakes;

        // The key is rotated at the server and then here. The session opened with the old key is still open.
        server.ExpectHeader(replacement);
        var replaced = await client.Put($"{space}/tool-connections/{id}/header", new ReplaceToolHeaderRequest(HeaderName, replacement));
        var refreshed = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections/{id}/refresh", new { });

        created.Problem.Should().BeNull();
        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        refreshed.Problem.Should().BeNull("the first call after the change carries the new header: the old session was let go");
        server.Handshakes.Should().Be(sessionsBefore + 1, "a new session was opened for it");
    }

    [Fact]
    public async Task A_credential_is_not_carried_to_wherever_a_tool_server_redirects()
    {
        var secret = NewPassword();
        await using var elsewhere = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless, HeaderName, secret));
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { RedirectTo = elsewhere.Url });
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);

        var created = await PostFor<ToolConnectionRefresh>(
            client,
            $"/api/spaces/{alice.SpaceId}/tool-connections",
            new CreateToolConnectionRequest("Moved", server.Url.ToString(), "header", HeaderName, secret));

        server.Requests.Should().BeGreaterThan(0);
        elsewhere.Requests.Should().Be(0, "a redirect is not followed, so the header goes only to the address a person entered");
        created.Problem.Should().NotBeNull();
        created.Connection.Status.Should().Be("failed");
        created.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task A_header_is_not_accepted_for_an_address_that_would_send_it_in_the_clear()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId, allowed: false);
        var space = $"/api/spaces/{alice.SpaceId}";

        var inTheClear = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Plain", "http://tools.example.test/mcp", "header", HeaderName, NewPassword()));
        var encrypted = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Safe", "https://tools.example.test/mcp", "header", HeaderName, NewPassword()));
        var noSecret = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Open", "http://tools.example.test/mcp"));

        inTheClear.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a header value is a secret, and http would send it readable to anyone on the way");
        (await inTheClear.Content.ReadAsStringAsync(Token)).Should().Contain("https");
        encrypted.StatusCode.Should().Be(HttpStatusCode.Created);
        noSecret.StatusCode.Should().Be(HttpStatusCode.Created, "an address with no credential has nothing to protect");
    }

    [Fact]
    public async Task A_tool_server_on_a_private_address_is_refused_when_the_space_does_not_allow_them()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId, allowed: false);

        var created = await PostFor<ToolConnectionRefresh>(
            client,
            $"/api/spaces/{alice.SpaceId}/tool-connections",
            new CreateToolConnectionRequest("Inside", server.Url.ToString()));

        created.Problem.Should().Contain("private addresses", "a tool server is reached through the checked outbound path, like a model");
        created.Connection.Status.Should().Be("failed");
        created.Tools.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    /// <summary>A loopback port nothing listens on: taken from the system, then let go.</summary>
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion
}
