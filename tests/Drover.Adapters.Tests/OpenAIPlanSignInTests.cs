namespace Drover.Adapters.Tests;

/// <summary>
/// What the ChatGPT plan sign-in sends and checks beyond the shared suite: the first registration and a later sign-in
/// with the issued client id, the exchange, the plan scope, every check on the ID token, refresh and sign-out, and
/// settings read for each request. Shapes from https://developers.openai.com/siwc/llms-full.txt ("Registration and
/// sign-in", "Accounts and sessions"), read 2026-11-27.
/// </summary>
public sealed class OpenAIPlanSignInTests
{
    #region Starting

    [Fact]
    public async Task A_first_sign_in_registers_with_the_entry_point_the_apps_name_a_host_id_and_the_resource()
    {
        var server = new OpenAIPlanAuthServer();
        var attempt = Attempt(server);

        var step = await ObservedPlanSignIn.Over(server).Begin(attempt, Token);

        step.Address.GetLeftPart(UriPartial.Path).Should().Be("https://auth.openai.com/api/accounts/authorize");
        step.Code.Should().BeNull();
        var query = OpenAIPlanAuthServer.Parse(step.Address.Query);
        query["client_id"].Should().Be("dynamic_agent_client");
        query["agent_name_hint"].Should().Be("Drover");
        query["ext_agent_host_id"].Should().StartWith("urn:uuid:");
        Guid.TryParse(query["ext_agent_host_id"]["urn:uuid:".Length..], out _).Should().BeTrue();
        query["response_type"].Should().Be("code");
        query["redirect_uri"].Should().Be(attempt.RedirectUri!.AbsoluteUri);
        query["scope"].Should().Be("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct");
        query["resource"].Should().Be("https://api.openai.com/v1");
        query["nonce"].Should().Be(attempt.Nonce);
        query.Should().NotContainKey("id_token_hint");
    }

    [Fact]
    public async Task A_later_sign_in_asks_with_the_issued_client_id_and_the_same_host_id_and_no_name()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var first = await SignedIn(server, signIn);
        var again = Attempt(server) with { Earlier = first.Details };

        var step = await signIn.Begin(again, Token);
        var tokens = await signIn.Finish(again, server.Approve(again.State), Token);

        var query = OpenAIPlanAuthServer.Parse(step.Address.Query);
        query["client_id"].Should().Be(OpenAIPlanAuthServer.IssuedClientId);
        query["ext_agent_host_id"].Should().Be(first.Details[OpenAIPlanSignIn.HostIdDetail]);
        query.Should().NotContainKey("agent_name_hint");
        tokens!.Details.Should().BeEquivalentTo(first.Details);
    }

    [Theory]
    [InlineData("http://127.0.0.1:1455/auth/callback", true)]
    [InlineData("http://127.0.0.1:54321/auth/callback", true)]
    [InlineData("http://localhost:1455/auth/callback", false)]
    [InlineData("https://127.0.0.1:1455/auth/callback", false)]
    [InlineData("http://127.0.0.1:1455/callback", false)]
    [InlineData("http://127.0.0.1:1455/auth/callback?next=1", false)]
    [InlineData("http://user@127.0.0.1:1455/auth/callback", false)]
    public void Only_the_loopback_return_address_on_any_port_is_accepted(string address, bool accepted)
    {
        var signIn = ObservedPlanSignIn.Over(new OpenAIPlanAuthServer());

        signIn.AcceptsRedirect(new Uri(address)).Should().Be(accepted);
        signIn.AcceptsRedirect(null).Should().BeFalse("the browser flow needs a device that listens");
    }

    #endregion

    #region Finishing

    [Fact]
    public async Task The_exchange_sends_the_issued_client_id_the_verifier_the_exact_return_address_and_the_resource()
    {
        var server = new OpenAIPlanAuthServer();
        var attempt = Attempt(server, port: 54321);

        var tokens = await Finish(server, ObservedPlanSignIn.Over(server), attempt);

        var exchange = server.Sent.Single(form => form.Field("grant_type") == "authorization_code");
        exchange.Address.Should().Be(new Uri("https://auth.openai.com/api/accounts/oauth/token"));
        exchange.Field("client_id").Should().Be(OpenAIPlanAuthServer.IssuedClientId);
        exchange.Field("code_verifier").Should().Be(attempt.Verifier);
        exchange.Field("redirect_uri").Should().Be("http://127.0.0.1:54321/auth/callback");
        exchange.Field("resource").Should().Be("https://api.openai.com/v1");
        exchange.Fields.Should().NotContainKey("client_secret");
        tokens.AccountLabel.Should().Be(OpenAIPlanAuthServer.Email);
        tokens.Details[OpenAIPlanSignIn.ClientIdDetail].Should().Be(OpenAIPlanAuthServer.IssuedClientId);
        tokens.Details[OpenAIPlanSignIn.SubjectDetail].Should().Be(OpenAIPlanAuthServer.Subject);
        tokens.Scopes.Should().Contain("chatgpt.tokens.use.direct");
        tokens.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
        tokens.Details.Values.Should().NotContain(value => value.Contains(tokens.AccessToken) || value.Contains(tokens.RefreshToken!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("dynamic_agent_client")]
    public async Task A_first_registration_that_comes_back_without_an_issued_client_id_is_refused_and_nothing_is_exchanged(string returned)
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var attempt = Attempt(server);
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, server.Approve(attempt.State, returned), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        server.TokenRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_later_sign_in_that_comes_back_with_another_client_id_is_refused_and_nothing_is_exchanged()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var first = await SignedIn(server, signIn);
        var again = Attempt(server) with { Earlier = first.Details };
        await signIn.Begin(again, Token);
        var before = server.TokenRequests.Count;

        var finishing = () => signIn.Finish(again, server.Approve(again.State, "oaiapp_someone_else"), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        server.TokenRequests.Should().HaveCount(before);
    }

    [Theory]
    [InlineData("email openid profile offline_access")]
    [InlineData("")]
    public async Task A_sign_in_that_did_not_allow_plan_use_keeps_the_registration_revokes_the_session_and_asks_again_for_consent(string granted)
    {
        var server = new OpenAIPlanAuthServer { GrantedScope = granted };
        var signIn = ObservedPlanSignIn.Over(server);
        var attempt = Attempt(server);
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, server.Approve(attempt.State), Token);

        var refused = (await finishing.Should().ThrowAsync<ModelSignInRefusedException>()).Which;
        refused.Message.Should().Be("ChatGPT signed you in but did not allow plan use. Try again and allow it.");
        refused.Kept.Should().Contain(OpenAIPlanSignIn.ClientIdDetail, OpenAIPlanAuthServer.IssuedClientId)
            .And.Contain(OpenAIPlanSignIn.SubjectDetail, OpenAIPlanAuthServer.Subject)
            .And.Contain(OpenAIPlanSignIn.PlanUseDetail, OpenAIPlanSignIn.PlanUseNotAllowedValue)
            .And.ContainKey(OpenAIPlanSignIn.HostIdDetail);
        server.Sent.Should().Contain(form => form.Address == server.Settings.RevokeAddress, "the unused session is ended");

        server.GrantedScope = "chatgpt.tokens.use.direct email offline_access openid profile resource.invoke";
        var again = Attempt(server) with { Earlier = refused.Kept };
        var step = await signIn.Begin(again, Token);
        var tokens = await signIn.Finish(again, server.Approve(again.State), Token);

        var query = OpenAIPlanAuthServer.Parse(step.Address.Query);
        query["client_id"].Should().Be(OpenAIPlanAuthServer.IssuedClientId, "no new registration is made");
        query["prompt"].Should().Be("consent");
        query["scope"].Should().Contain("chatgpt.tokens.use.direct", "the complete scope set is asked for again");
        tokens!.Details.Should().NotContainKey(OpenAIPlanSignIn.PlanUseDetail);
    }

    [Theory]
    [InlineData(IdTokenFlaw.SignedByAnotherKey)]
    [InlineData(IdTokenFlaw.WrongNonce)]
    [InlineData(IdTokenFlaw.WrongAudience)]
    [InlineData(IdTokenFlaw.WrongIssuer)]
    [InlineData(IdTokenFlaw.Expired)]
    [InlineData(IdTokenFlaw.Missing)]
    public async Task An_id_token_that_fails_any_check_is_refused_and_no_token_set_comes_back(IdTokenFlaw flaw)
    {
        var server = new OpenAIPlanAuthServer { Flaw = flaw };
        var signIn = ObservedPlanSignIn.Over(server);
        var attempt = Attempt(server);
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, server.Approve(attempt.State), Token);

        (await finishing.Should().ThrowAsync<ModelSignInRefusedException>()).Which.Message.Should().Be("Sign-in was cancelled.");
        server.Sent.Should().Contain(form => form.Address == server.Settings.RevokeAddress, "a session that is not kept is ended");
    }

    [Fact]
    public async Task A_later_sign_in_as_another_account_is_refused()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var first = await SignedIn(server, signIn);
        server.TokenSubject = "another-subject";
        var again = Attempt(server) with { Earlier = first.Details };
        await signIn.Begin(again, Token);

        var finishing = () => signIn.Finish(again, server.Approve(again.State), Token);

        (await finishing.Should().ThrowAsync<ModelSignInRefusedException>()).Which.Message
            .Should().Be("That is a different ChatGPT account. Sign out first.");
        server.Sent[^1].Address.Should().Be(server.Settings.RevokeAddress);
    }

    [Fact]
    public async Task A_return_that_names_its_state_twice_is_refused()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var attempt = Attempt(server);
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, new ModelSignInReturn($"{server.Approve(attempt.State).Text}&state=other"), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        server.TokenRequests.Should().BeEmpty();
    }

    #endregion

    #region Refreshing and Signing Out

    [Fact]
    public async Task A_refresh_sends_the_issued_client_id_the_refresh_token_and_the_resource_and_no_scope()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var tokens = await SignedIn(server, signIn);

        var renewed = await signIn.Refresh(tokens, Token);

        var refresh = server.Sent.Single(form => form.Field("grant_type") == "refresh_token");
        refresh.Field("client_id").Should().Be(OpenAIPlanAuthServer.IssuedClientId);
        refresh.Field("refresh_token").Should().Be(tokens.RefreshToken);
        refresh.Field("resource").Should().Be("https://api.openai.com/v1");
        refresh.Fields.Should().NotContainKey("scope");
        renewed.RefreshToken.Should().NotBe(tokens.RefreshToken);
        renewed.Details.Should().BeEquivalentTo(tokens.Details);
        renewed.AccountLabel.Should().Be(tokens.AccountLabel);
    }

    [Fact]
    public async Task Signing_out_revokes_the_refresh_token_at_the_revocation_address()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var tokens = await SignedIn(server, signIn);

        await signIn.Revoke(tokens, Token);

        var revoke = server.Sent[^1];
        revoke.Address.Should().Be(new Uri("https://auth.openai.com/api/accounts/oauth/revoke"));
        revoke.Field("token").Should().Be(tokens.RefreshToken);
        revoke.Field("token_type_hint").Should().Be("refresh_token");
        revoke.Field("client_id").Should().Be(OpenAIPlanAuthServer.IssuedClientId);
    }

    [Fact]
    public async Task Changing_a_setting_changes_the_next_request_with_no_rebuild()
    {
        var server = new OpenAIPlanAuthServer();
        var settings = new SettableOptions<OpenAIPlanOptions>(server.Settings);
        var signIn = ObservedPlanSignIn.Over(server, settings: settings);
        var tokens = await SignedIn(server, signIn);

        settings.CurrentValue = new OpenAIPlanOptions
        {
            AuthorizeAddress = new Uri("https://auth.example.test/authorize"),
            RegistrationClientId = "another_entry_point",
            RevokeAddress = new Uri("https://auth.example.test/revoke"),
        };
        var step = await signIn.Begin(Attempt(server), Token);
        // The fake server answers only at its own addresses, so the revoke sent elsewhere is a failure here.
        await FluentActions.Awaiting(() => signIn.Revoke(tokens, Token)).Should().ThrowAsync<ModelProviderException>();

        step.Address.GetLeftPart(UriPartial.Path).Should().Be("https://auth.example.test/authorize");
        OpenAIPlanAuthServer.Parse(step.Address.Query)["client_id"].Should().Be("another_entry_point");
        server.Sent[^1].Address.Should().Be(new Uri("https://auth.example.test/revoke"));
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelSignInAttempt Attempt(OpenAIPlanAuthServer server, int? port = null) =>
        new(Guid.NewGuid(), Random(), Random(), Random(), new UriBuilder(server.Settings.RedirectUri) { Port = port ?? 1455 }.Uri);

    private static async Task<ModelTokens> SignedIn(OpenAIPlanAuthServer server, ObservedPlanSignIn signIn) =>
        await Finish(server, signIn, Attempt(server));

    private static async Task<ModelTokens> Finish(OpenAIPlanAuthServer server, ObservedPlanSignIn signIn, ModelSignInAttempt attempt)
    {
        await signIn.Begin(attempt, Token);
        return await signIn.Finish(attempt, server.Approve(attempt.State), Token) ?? throw new InvalidOperationException("No token set.");
    }

    private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    #endregion
}
