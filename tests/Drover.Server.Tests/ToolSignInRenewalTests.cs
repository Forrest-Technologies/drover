namespace Drover.Server.Tests;

/// <summary>
/// The background renewal of tool server sign-ins when things go wrong: a server that is down, and a sign-in that
/// cannot even be prepared. Neither may take the passes away from the sign-ins that can be renewed.
/// </summary>
public sealed class ToolSignInRenewalTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:PublicUrl"] = "https://drover.example.test",

        // Two a pass, so a test can fill a pass with two sign-ins.
        ["Drover:Policy:Rules:MaxToolSignInRenewalsPerPass"] = "2",
    };

    #endregion

    #region Tests

    [Fact]
    public async Task A_server_that_is_down_does_not_take_every_pass_from_the_sign_ins_that_can_be_renewed()
    {
        await using var dead = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        await using var alive = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var toDead = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Mail", dead.Url.ToString(), "oauth"))).Connection.Id;
        var toAlive = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", alive.Url.ToString(), "oauth"))).Connection.Id;

        // Two people's sign-ins to the server that will die run out first, and fill a pass of two between them.
        await SignInToServer(admin, dead, space, toDead);
        await SignInToServer(asMember, dead, space, toDead);
        await SignInToServer(admin, alive, space, toAlive);
        var renewal = Services.GetRequiredService<ToolSignInRenewal>();
        await dead.ShutDown();

        var first = await renewal.RenewDue(Token);
        var second = await renewal.RenewDue(Token);
        var third = await renewal.RenewDue(Token);
        var forAdmin = await (await admin.Get($"{space}/tool-connections")).Content.ReadAsStringAsync(Token);

        first.Should().Be(0, "the pass was spent on the server that is down");
        second.Should().Be(1, "the next pass goes to the sign-in that has not been tried, not to the two that just failed");
        third.Should().Be(0, "nothing is left to renew, and the ones that failed are not tried again so soon");
        alive.Refreshes.Should().Be(1);
        Summary(forAdmin, toDead).SignIn.Should().Be("signed_in", "a server that is down has refused nothing");
    }

    [Fact]
    public async Task One_space_with_more_sign_ins_than_a_pass_holds_does_not_keep_another_spaces_sign_in_waiting()
    {
        await using var crowded = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        await using var other = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var admin = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        await AllowPrivateAddresses(bob.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var bobs = $"/api/spaces/{bob.SpaceId}";
        var toCrowded = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Mail", crowded.Url.ToString(), "oauth"))).Connection.Id;
        var toOther = (await PostFor<ToolConnectionRefresh>(asBob, $"{bobs}/tool-connections", new CreateToolConnectionRequest("Calendar", other.Url.ToString(), "oauth"))).Connection.Id;

        // Three sign-ins in one space, all running out before the one in the other space, and a pass holds two.
        await SignInToServer(admin, crowded, space, toCrowded);

        foreach (var name in (string[])["first", "second"])
        {
            var member = await SeedMember(alice.SpaceId, name, MembershipRole.Member);
            await SignInToServer(await SignIn(member.Email, member.Password), crowded, space, toCrowded);
        }

        await SignInToServer(asBob, other, bobs, toOther);

        var renewed = await Services.GetRequiredService<ToolSignInRenewal>().RenewDue(Token);

        renewed.Should().Be(2);
        other.Refreshes.Should().Be(1, "the first pass takes one sign-in from each space before it takes a second from any");
        crowded.Refreshes.Should().Be(1);
    }

    [Fact]
    public async Task A_sign_in_that_was_just_renewed_is_left_alone_until_the_lead_has_passed()
    {
        // Its tokens last less than the lead, so by its end alone it is due again the moment it is renewed.
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        var alice = await SeedPerson("alice");
        var admin = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var id = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"))).Connection.Id;
        await SignInToServer(admin, server, space, id);
        var renewal = Services.GetRequiredService<ToolSignInRenewal>();

        var first = await renewal.RenewDue(Token);
        var askedByTheFirst = server.Requests;
        Time.Advance(TimeSpan.FromMinutes(5));
        var nextPass = await renewal.RenewDue(Token);
        var askedByTheNext = server.Requests;
        Time.Advance(TimeSpan.FromMinutes(11));
        await renewal.RenewDue(Token);

        first.Should().Be(1);
        nextPass.Should().Be(0);
        askedByTheNext.Should().Be(askedByTheFirst, "a sign-in renewed within the lead is not looked at, so its server is asked nothing");
        server.Requests.Should().BeGreaterThan(askedByTheNext, "once the lead has passed it is looked at again");
    }

    [Fact]
    public async Task A_renewal_that_failed_once_is_tried_again_before_the_sign_in_runs_out()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        var alice = await SeedPerson("alice");
        var admin = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var id = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"))).Connection.Id;
        await SignInToServer(admin, server, space, id);
        var renewal = Services.GetRequiredService<ToolSignInRenewal>();
        server.FailRenewals(503);

        var whileFailing = await renewal.RenewDue(Token);
        var rightAway = await renewal.RenewDue(Token);
        server.FailRenewals(null);
        Time.Advance(TimeSpan.FromMinutes(6));
        var later = await renewal.RenewDue(Token);

        whileFailing.Should().Be(0);
        rightAway.Should().Be(0, "one that just failed waits");
        later.Should().Be(1, "but not longer than the lead, or a single failure would mean the sign-in runs out unrenewed");
    }

    [Fact]
    public async Task A_sign_in_that_cannot_be_prepared_fails_alone_and_the_rest_of_the_pass_goes_on()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = ShortLived });
        var alice = await SeedPerson("alice");
        var admin = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var id = (await PostFor<ToolConnectionRefresh>(admin, $"{space}/tool-connections", new CreateToolConnectionRequest("Calendar", server.Url.ToString(), "oauth"))).Connection.Id;
        await SignInToServer(admin, server, space, id);
        await SeedBrokenSignIn(alice.SpaceId, alice.UserId);

        var renewed = await Services.GetRequiredService<ToolSignInRenewal>().RenewDue(Token);

        renewed.Should().Be(1, "the sign-in whose connection points at a client secret that is gone fails by itself");
        server.Refreshes.Should().Be(1);
    }

    #endregion

    #region Helpers

    private static ToolServerSignIn ShortLived => new() { TokenLifetime = TimeSpan.FromMinutes(10) };

    private static ToolConnectionSummary Summary(string listBody, Guid connectionId) =>
        JsonSerializer.Deserialize<List<ToolConnectionSummary>>(listBody, JsonSerializerOptions.Web)!.Single(connection => connection.Id == connectionId);

    private static async Task SignInToServer(ApiClient person, SdkToolServer server, string space, Guid connectionId)
    {
        var start = await PostFor<ToolSignInStart>(person, $"{space}/tool-connections/{connectionId}/sign-in", new { });
        start.Step.Should().Be("redirect", start.Problem);
        var answer = await server.Approve(new Uri(start.AuthorizationUrl!));
        var back = await person.Get(
            $"/oauth/mcp/callback?code={Uri.EscapeDataString(answer.Code)}&state={Uri.EscapeDataString(answer.State ?? string.Empty)}&iss={Uri.EscapeDataString(answer.Issuer ?? string.Empty)}");
        back.Headers.Location!.ToString().Should().StartWith("/?tool-sign-in=connected");
    }

    /// <summary>
    /// A sign-in that runs out sooner than any other and cannot be prepared: its connection names a client secret
    /// that is not in the vault. Reading it throws before anything is sent.
    /// </summary>
    private async Task SeedBrokenSignIn(Guid spaceId, Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var connection = new ToolConnection
        {
            Name = "Broken",
            Slug = "broken",
            Url = "https://tools.example.test/mcp",
            AuthKind = ToolAuthKind.Oauth,
            CredentialScope = CredentialScope.PerUser,
            Status = ConnectionStatus.Ready,
            Oauth = new OAuthMetadata { ClientId = "known", Issuer = "https://sign-in.example.test", ClientSecretId = Guid.CreateVersion7() },
        };
        var secretId = await scope.ServiceProvider.GetRequiredService<ISecretVault>().Store(SecretKind.OauthTokens, "{}", userId, Token);
        db.ToolConnections.Add(connection);
        db.ToolConnectionCredentials.Add(
            new ToolConnectionCredential { ToolConnectionId = connection.Id, SecretId = secretId, AudienceUserId = userId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    #endregion
}
