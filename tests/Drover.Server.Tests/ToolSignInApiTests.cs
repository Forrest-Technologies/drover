namespace Drover.Server.Tests;

/// <summary>
/// Signing in to a tool server, end to end: the real adapter and protocol library, a real tool server with its own
/// sign-in on loopback, the real callback, vault, worker, and API. The test plays the person's browser.
/// </summary>
public sealed class ToolSignInApiTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:PublicUrl"] = "https://drover.example.test",
    };

    #endregion

    #region Tests

    [Theory]
    [InlineData(ServerGeneration.Stateless)]
    [InlineData(ServerGeneration.SessionBased)]
    public async Task A_person_signs_in_and_a_bot_acts_with_their_tokens_and_nobody_elses_and_no_token_is_shown(ServerGeneration generation)
    {
        Script = new ScriptedModel(
            [new ToolCallOutput("c1", "calendar__echo", """{"text":"mine"}""")],
            [new TextOutput("Done.")],
            [new ToolCallOutput("c2", "calendar__echo", """{"text":"not mine"}""")],
            [new TextOutput("I could not.")]);
        await using var server = await SdkToolServer.Start(new ToolServerSpec(generation) { SignIn = new ToolServerSignIn() });
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";

        var createdBody = await (await admin.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"))).Content.ReadAsStringAsync(Token);
        var created = JsonSerializer.Deserialize<ToolConnectionRefresh>(createdBody, JsonSerializerOptions.Web)!;
        var id = created.Connection.Id;
        var startBody = await (await asMember.Post($"{space}/tool-connections/{id}/sign-in")).Content.ReadAsStringAsync(Token);
        var start = JsonSerializer.Deserialize<ToolSignInStart>(startBody, JsonSerializerOptions.Web)!;
        var back = await ComeBack(asMember, await server.Approve(new Uri(start.AuthorizationUrl!)));
        var forMemberBody = await (await asMember.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token);
        var forAdminBody = await (await admin.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token);
        var toolsBody = await (await admin.Get($"{space}/tool-connections/{id}/tools")).Content.ReadAsStringAsync(Token);

        created.Connection.Should().Match<ToolConnectionSummary>(connection =>
            connection.AuthKind == "oauth" && connection.CredentialScope == "per_user" && connection.SignIn == "needs_sign_in" && connection.Status == "pending");
        created.Problem.Should().Contain("Sign in");
        start.Step.Should().Be("redirect", start.Problem);
        back.Should().StartWith("/?tool-sign-in=connected");
        Summary(forMemberBody, id).Should().Match<ToolConnectionSummary>(connection => connection.SignIn == "signed_in" && connection.Status == "ready");
        Summary(forAdminBody, id).SignIn.Should().Be("needs_sign_in", "one person's sign-in is nobody else's");
        toolsBody.Should().Contain("calendar__echo", "the first sign-in lists the server's tools into the space's catalog");

        var bot = await CreateBot(admin, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");
        var callsBefore = server.Requests;
        var mine = await Ask(asMember, alice.SpaceId, bot.Id, "Use my calendar");
        var callsForMember = server.Requests - callsBefore;
        var notMine = await Ask(admin, alice.SpaceId, bot.Id, "Use my calendar");
        var events = await (await asMember.Get($"{space}/runs/{mine.Id}/events")).Content.ReadAsStringAsync(Token);

        mine.Status.Should().Be("completed", mine.Error?.Message);
        Script.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"echo: mine\""));
        callsForMember.Should().BeGreaterThan(0);
        notMine.Status.Should().Be("completed", notMine.Error?.Message);
        Script.Requests[3].Should().Contain(message => message.Role == "tool" && message.Summary.Contains("needs you to sign in", StringComparison.Ordinal));
        server.Requests.Should().Be(callsBefore + callsForMember, "a run for a person who has not signed in never reaches the tool server");
        server.Approvals.Should().Be(1);

        var everythingShown = string.Join('\n', createdBody, startBody, back, forMemberBody, forAdminBody, toolsBody, events, string.Join('\n', Logs.Entries.Select(entry => entry.ToString())));
        server.IssuedSecrets.Should().NotBeEmpty().And.AllSatisfy(secret => everythingShown.Should().NotContain(secret, "no code or token is in a response, an event, or a log"));
    }

    [Fact]
    public async Task A_callback_completes_only_the_sign_in_of_the_person_who_started_it_and_only_once()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = new ToolServerSignIn() });
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var admin = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var created = await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"));
        var id = created.Connection.Id;
        var start = await PostFor<ToolSignInStart>(asMember, $"{space}/tool-connections/{id}/sign-in", new { });
        var answer = await server.Approve(new Uri(start.AuthorizationUrl!));

        var signedOut = await NewClient().Get(CallbackPath(answer));
        var byOutsider = await ComeBack(asOutsider, answer);
        var byAnotherMember = await ComeBack(admin, answer);
        var withAnotherState = await ComeBack(asMember, answer with { State = "not-the-state" });
        var afterTheOthers = Summary(await (await asMember.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token), id).SignIn;
        var byThePerson = await ComeBack(asMember, answer);
        var again = await ComeBack(asMember, answer);
        var fromOtherSpace = await asOutsider.Post($"{space}/tool-connections/{id}/sign-in");
        var inOwnSpace = await asOutsider.Post($"/api/spaces/{outsider.SpaceId}/tool-connections/{id}/sign-in");

        signedOut.StatusCode.Should().NotBe(HttpStatusCode.Redirect, "nobody is signed in, so nothing is completed");
        signedOut.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Found, HttpStatusCode.Forbidden);
        byOutsider.Should().Be("/?tool-sign-in=failed", "a stranger learns nothing, not even which space the sign-in is in");
        byAnotherMember.Should().Be("/?tool-sign-in=failed");
        withAnotherState.Should().Be("/?tool-sign-in=failed");
        afterTheOthers.Should().Be("needs_sign_in", "none of those completed anything");
        byThePerson.Should().StartWith("/?tool-sign-in=connected");
        again.Should().StartWith("/?tool-sign-in=failed", "a sign-in is completed once");
        fromOtherSpace.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        inOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        server.Approvals.Should().Be(1);
    }

    [Fact]
    public async Task When_consent_is_withdrawn_the_bot_is_told_the_sign_in_reads_needs_reconnect_and_signing_in_again_mends_it()
    {
        Script = new ScriptedModel(
            [new ToolCallOutput("c1", "calendar__echo", """{"text":"one"}""")],
            [new TextOutput("I could not.")],
            [new ToolCallOutput("c2", "calendar__echo", """{"text":"two"}""")],
            [new TextOutput("Done.")]);
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = new ToolServerSignIn() });
        var alice = await SeedPerson("alice");
        var admin = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var created = await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"));
        var id = created.Connection.Id;
        await SignInToServer(admin, server, space, id);
        var bot = await CreateBot(admin, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");

        server.WithdrawConsent();
        var refused = await Ask(admin, alice.SpaceId, bot.Id, "Use my calendar");
        var afterRefusal = Summary(await (await admin.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token), id);
        await SignInToServer(admin, server, space, id);
        var afterSignIn = Summary(await (await admin.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token), id);
        var mended = await Ask(admin, alice.SpaceId, bot.Id, "Use my calendar");

        refused.Status.Should().Be("completed", refused.Error?.Message);
        Script.Requests[1].Should().Contain(message => message.Role == "tool" && message.Summary.Contains("sign in again", StringComparison.Ordinal));
        afterRefusal.SignIn.Should().Be("needs_reconnect");
        afterRefusal.Status.Should().Be("ready", "one person's dead sign-in says nothing about the connection");
        afterSignIn.SignIn.Should().Be("signed_in");
        mended.Status.Should().Be("completed", mended.Error?.Message);
        Script.Requests[3].Should().Contain(new SeenMessage("tool", "c2 = \"echo: two\""));
        server.Approvals.Should().Be(2);
    }

    [Fact]
    public async Task One_sign_in_for_the_space_is_an_admins_to_make_and_to_remove_and_serves_every_member()
    {
        Script = new ScriptedModel([new ToolCallOutput("c1", "calendar__echo", """{"text":"shared"}""")], [new TextOutput("Done.")]);
        var clientId = $"known-{Guid.CreateVersion7():N}";
        var clientSecret = NewPassword();
        await using var server = await SdkToolServer.Start(
            new ToolServerSpec(ServerGeneration.Stateless) { SignIn = new ToolServerSignIn { KnownClientId = clientId, RegistersClients = false } });
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var request = new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth", ClientId: clientId, ClientSecret: clientSecret, CredentialScope: "space");

        var createdBody = await (await admin.Post($"{space}/tool-connections", request)).Content.ReadAsStringAsync(Token);
        var id = JsonSerializer.Deserialize<ToolConnectionRefresh>(createdBody, JsonSerializerOptions.Web)!.Connection.Id;
        var byMember = await asMember.Post($"{space}/tool-connections/{id}/sign-in");
        await SignInToServer(admin, server, space, id);
        var forMember = await (await asMember.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token);
        var bot = await CreateBot(admin, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");
        var run = await Ask(asMember, alice.SpaceId, bot.Id, "Use the shared calendar");
        var removedByMember = await asMember.Delete($"{space}/tool-connections/{id}/sign-in");
        var removed = await admin.Delete($"{space}/tool-connections/{id}/sign-in");
        var afterwards = Summary(await (await admin.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token), id);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a sign-in every bot run in the space will act as is an admin's decision");
        Summary(forMember, id).Should().Match<ToolConnectionSummary>(connection => connection.CredentialScope == "space" && connection.SignIn == "signed_in");
        run.Status.Should().Be("completed", run.Error?.Message);
        Script.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"echo: shared\""));
        server.ClientsSeen.Should().Equal(clientId);
        server.Registrations.Should().Be(0, "a client id an admin entered is used before any other way");
        removedByMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterwards.SignIn.Should().Be("needs_sign_in");
        string.Join('\n', createdBody, forMember, string.Join('\n', Logs.Entries.Select(entry => entry.ToString())))
            .Should().NotContain(clientSecret, "a client secret goes to the vault and nowhere else");
    }

    [Fact]
    public async Task A_sign_in_connection_needs_https_like_a_header_and_validates_its_client()
    {
        var alice = await SeedPerson("alice");
        var admin = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId, allowed: false);
        var space = $"/api/spaces/{alice.SpaceId}";

        var inTheClear = await admin.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", "http://tools.example.test/mcp", "oauth"));
        var secretAlone = await admin.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", "https://tools.example.test/mcp", "oauth", ClientSecret: NewPassword()));
        var unknownScope = await admin.Post($"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", "https://tools.example.test/mcp", "oauth", CredentialScope: "everyone"));
        var notASignIn = await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Plain", "https://tools.example.test/mcp"));
        var signInToPlain = await PostFor<ToolSignInStart>(admin, $"{space}/tool-connections/{notASignIn.Connection.Id}/sign-in", new { });
        var document = await NewClient().Get("/oauth/client.json");
        var described = await document.Content.ReadAsStringAsync(Token);

        inTheClear.StatusCode.Should().Be(HttpStatusCode.BadRequest, "tokens would travel where others can read them");
        secretAlone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        notASignIn.Connection.SignIn.Should().BeNull();
        signInToPlain.Step.Should().Be("failed");
        document.StatusCode.Should().Be(HttpStatusCode.OK, "a tool server that reads client documents fetches this without signing in");
        described.Should().Contain("https://drover.example.test/oauth/client.json").And.Contain("https://drover.example.test/oauth/mcp/callback").And.Contain("\"none\"");
    }

    #endregion

    #region Helpers

    private static string CallbackPath(SignInAnswer answer) =>
        $"/oauth/mcp/callback?code={Uri.EscapeDataString(answer.Code)}&state={Uri.EscapeDataString(answer.State ?? string.Empty)}&iss={Uri.EscapeDataString(answer.Issuer ?? string.Empty)}";

    /// <summary>The person's browser arriving back from the tool server's sign-in page. Returns where Drover sends it next.</summary>
    private static async Task<string> ComeBack(ApiClient browser, SignInAnswer answer)
    {
        var response = await browser.Get(CallbackPath(answer));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync(Token));
        return response.Headers.Location!.ToString();
    }

    private static async Task SignInToServer(ApiClient person, SdkToolServer server, string space, Guid connectionId)
    {
        var start = await PostFor<ToolSignInStart>(person, $"{space}/tool-connections/{connectionId}/sign-in", new { });
        start.Step.Should().Be("redirect", start.Problem);
        (await ComeBack(person, await server.Approve(new Uri(start.AuthorizationUrl!)))).Should().StartWith("/?tool-sign-in=connected");
    }

    private static ToolConnectionSummary Summary(string listBody, Guid connectionId) =>
        JsonSerializer.Deserialize<List<ToolConnectionSummary>>(listBody, JsonSerializerOptions.Web)!.Single(connection => connection.Id == connectionId);

    private static async Task<RunSummary> Ask(ApiClient person, Guid spaceId, Guid botId, string text)
    {
        var space = $"/api/spaces/{spaceId}";
        var thread = await PostFor<ThreadSummary>(person, $"{space}/bots/{botId}/threads", new CreateThreadRequest(null));
        var sent = await PostFor<SendMessageResponse>(person, $"{space}/threads/{thread.Id}/messages", new SendMessageRequest(text));
        return await WaitForRun(person, spaceId, sent.Run.Id);
    }

    #endregion
}
