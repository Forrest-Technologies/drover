namespace Drover.Adapters.Tests;

/// <summary>
/// What the Claude plan sign-in puts on the wire and what it takes back, against the hand-written token address of
/// <see cref="AnthropicTokenServer"/>. The shapes are those of the provider's working client (see
/// <see cref="AnthropicPlanSignInOptions"/>): the address's parameters, the JSON exchange and refresh, the code page for a
/// device that cannot listen, and the three ways a person may paste what that page shows.
/// </summary>
public sealed class AnthropicPlanSignInWireTests
{
    #region Starting

    [Fact]
    public async Task The_address_carries_every_parameter_the_provider_asks_for_from_the_settings()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var attempt = Attempt(new Uri("http://127.0.0.1:50999/callback"));

        var step = await rig.SignIn.Begin(attempt, Token);

        step.Wait.Should().Be(ModelSignInWait.Return);
        step.Address.GetLeftPart(UriPartial.Path).Should().Be("https://claude.ai/oauth/authorize");
        var query = Query(step.Address);
        query["code"].Should().Be("true");
        query["client_id"].Should().Be("9d1c250a-e61b-44d9-88ed-5944d1962f5e");
        query["response_type"].Should().Be("code");
        query["redirect_uri"].Should().Be("http://localhost:50999/callback", "the provider's registered loopback host, on the device's own port");
        query["scope"].Should().Be("org:create_api_key user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload");
        query["code_challenge_method"].Should().Be("S256");
        query["state"].Should().Be(attempt.State).And.NotBe(attempt.Verifier, "the verifier never travels in an address");
    }

    [Fact]
    public async Task A_device_that_cannot_listen_is_sent_back_to_the_providers_code_page()
    {
        var rig = AnthropicPlanSignInRig.Start();

        var step = await rig.SignIn.Begin(Attempt(null), Token);

        step.Wait.Should().Be(ModelSignInWait.Paste);
        Query(step.Address)["redirect_uri"].Should().Be("https://platform.claude.com/oauth/code/callback");
    }

    [Fact]
    public void Only_a_loopback_address_on_the_configured_path_or_none_is_accepted()
    {
        var rig = AnthropicPlanSignInRig.Start();

        rig.SignIn.AcceptsRedirect(null).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new Uri("http://localhost:53692/callback")).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new Uri("http://127.0.0.1:41000/callback")).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new Uri("https://localhost:53692/callback")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("http://localhost:53692/callback?next=elsewhere")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("http://drover.example.test:53692/callback")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("https://platform.claude.com/oauth/code/callback")).Should().BeFalse("the code page is the provider's, never a device's");
    }

    #endregion

    #region Finishing

    [Fact]
    public async Task A_pasted_code_and_state_a_bare_code_and_a_whole_return_address_each_finish_the_sign_in()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var attempt = Attempt(null);
        await rig.SignIn.Begin(attempt, Token);

        List<ModelSignInReturn> pasted =
        [
            new($"{rig.Server.IssueCode()}#{attempt.State}"),
            new($"  {rig.Server.IssueCode()}\n"),
            new($"https://platform.claude.com/oauth/code/callback?code={rig.Server.IssueCode()}&state={Uri.EscapeDataString(attempt.State)}"),
        ];

        foreach (var text in pasted)
        {
            (await rig.SignIn.Finish(attempt, text, Token)).Should().NotBeNull();
        }

        rig.Server.Received.Should().HaveCount(3).And.OnlyContain(request => request.Grant == "authorization_code");
    }

    [Fact]
    public async Task A_pasted_address_or_code_with_another_state_is_refused_and_nothing_is_exchanged()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var attempt = Attempt(null);

        var withHash = () => rig.SignIn.Finish(attempt, new($"{rig.Server.IssueCode()}#another-state"), Token);
        var asAddress = () => rig.SignIn.Finish(attempt, new($"https://platform.claude.com/oauth/code/callback?code={rig.Server.IssueCode()}&state=another-state"), Token);

        await withHash.Should().ThrowAsync<ModelSignInRefusedException>();
        await asAddress.Should().ThrowAsync<ModelSignInRefusedException>();
        rig.Server.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_browser_return_that_names_no_state_is_refused()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var attempt = Attempt(new Uri("http://localhost:53692/callback"));

        var finishing = () => rig.SignIn.Finish(attempt, new($"code={rig.Server.IssueCode()}"), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        rig.Server.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_exchange_is_json_with_the_attempts_state_verifier_and_the_same_return_address_and_reads_the_expiry()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
        var rig = AnthropicPlanSignInRig.Start(time);
        var attempt = Attempt(new Uri("http://127.0.0.1:50999/callback"));
        var code = rig.Server.IssueCode();

        var tokens = await rig.SignIn.Finish(attempt, new($"?code={code}&state={Uri.EscapeDataString(attempt.State)}"), Token);

        var sent = rig.Server.Sent.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.Should().Be(new Uri("https://platform.claude.com/v1/oauth/token"));
        sent.Headers.Should().NotContainKey("Authorization");
        var body = sent.Json;
        body.GetProperty("grant_type").GetString().Should().Be("authorization_code");
        body.GetProperty("client_id").GetString().Should().Be("9d1c250a-e61b-44d9-88ed-5944d1962f5e");
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("state").GetString().Should().Be(attempt.State);
        body.GetProperty("redirect_uri").GetString().Should().Be("http://localhost:50999/callback");
        body.GetProperty("code_verifier").GetString().Should().Be(attempt.Verifier);
        tokens!.ExpiresAt.Should().Be(time.GetUtcNow().AddHours(8));
        tokens.Scopes.Should().Equal("user:inference", "user:profile");
        tokens.AccountLabel.Should().BeNull();
    }

    [Fact]
    public async Task An_exchange_the_provider_will_not_make_is_a_refusal_and_one_it_cannot_make_now_is_not()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var attempt = Attempt(null);

        rig.Server.ExchangeStatus = HttpStatusCode.BadRequest;
        var refused = () => rig.SignIn.Finish(attempt, new(rig.Server.IssueCode()), Token);
        await refused.Should().ThrowAsync<ModelSignInRefusedException>();

        rig.Server.ExchangeStatus = HttpStatusCode.BadGateway;
        var failed = () => rig.SignIn.Finish(attempt, new(rig.Server.IssueCode()), Token);
        (await failed.Should().ThrowAsync<ModelProviderException>()).Which.Failure.Should().Be(ModelFailure.Unreachable);
    }

    #endregion

    #region Refreshing

    [Fact]
    public async Task A_refresh_is_json_with_the_client_id_and_the_refresh_token_and_rotates()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var first = await SignedIn(rig);

        var second = await rig.SignIn.Refresh(first, Token);

        var body = rig.Server.Sent[^1].Json;
        body.GetProperty("grant_type").GetString().Should().Be("refresh_token");
        body.GetProperty("client_id").GetString().Should().Be("9d1c250a-e61b-44d9-88ed-5944d1962f5e");
        body.GetProperty("refresh_token").GetString().Should().Be(first.RefreshToken);
        body.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("grant_type", "client_id", "refresh_token");
        second.RefreshToken.Should().NotBe(first.RefreshToken);
    }

    [Fact]
    public async Task A_refresh_answer_with_no_refresh_token_keeps_the_stored_one()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var first = await SignedIn(rig);
        rig.Server.RotatesRefreshTokens = false;

        var second = await rig.SignIn.Refresh(first, Token);

        second.AccessToken.Should().NotBe(first.AccessToken);
        second.RefreshToken.Should().Be(first.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_grant")]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_grant")]
    [InlineData(HttpStatusCode.Forbidden, "refresh_token_invalidated")]
    public async Task A_400_401_or_403_naming_an_ended_grant_ends_the_sign_in_in_either_error_shape(HttpStatusCode status, string code)
    {
        foreach (var shape in new Func<string, string>[]
        {
            error => JsonSerializer.Serialize(new { error, error_description = "over" }),
            error => JsonSerializer.Serialize(new { type = "error", error = new { type = error, message = "over" } }),
        })
        {
            var rig = AnthropicPlanSignInRig.Start();
            var tokens = await SignedIn(rig);
            rig.Server.RefreshAnswer = SignInServerAnswer.Ended;
            rig.Server.EndedStatus = status;
            rig.Server.EndedCode = code;
            rig.Server.EndedBody = shape;

            var refreshing = () => rig.SignIn.Refresh(tokens, Token);

            await refreshing.Should().ThrowAsync<SignInEndedException>();
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData(HttpStatusCode.TooManyRequests, "invalid_grant")]
    [InlineData(HttpStatusCode.InternalServerError, "invalid_grant")]
    public async Task Any_other_refresh_failure_may_pass_and_does_not_end_the_sign_in(HttpStatusCode status, string code)
    {
        var rig = AnthropicPlanSignInRig.Start();
        var tokens = await SignedIn(rig);
        rig.Server.RefreshAnswer = SignInServerAnswer.Ended;
        rig.Server.EndedStatus = status;
        rig.Server.EndedCode = code;

        var refreshing = () => rig.SignIn.Refresh(tokens, Token);

        await refreshing.Should().ThrowAsync<ModelProviderException>();
    }

    #endregion

    #region Settings

    [Fact]
    public async Task A_changed_client_id_and_addresses_reach_the_next_request_without_a_rebuild()
    {
        var rig = AnthropicPlanSignInRig.Start();
        var first = await SignedIn(rig);
        rig.Options.CurrentValue = new AnthropicPlanSignInOptions
        {
            ClientId = "changed-client",
            StartAddress = new Uri("https://approve.example.test/oauth/authorize"),
            TokenAddress = new Uri("https://tokens.example.test/v1/oauth/token"),
            CodePageRedirectUri = new Uri("https://approve.example.test/oauth/code/callback"),
            Scopes = "user:inference",
        };

        var step = await rig.SignIn.Begin(Attempt(null), Token);
        await rig.SignIn.Refresh(first, Token);

        step.Address.Host.Should().Be("approve.example.test");
        Query(step.Address)["client_id"].Should().Be("changed-client");
        Query(step.Address)["redirect_uri"].Should().Be("https://approve.example.test/oauth/code/callback");
        Query(step.Address)["scope"].Should().Be("user:inference");
        rig.Server.Sent[^1].Uri.Host.Should().Be("tokens.example.test");
        rig.Server.Sent[^1].Json.GetProperty("client_id").GetString().Should().Be("changed-client");
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelSignInAttempt Attempt(Uri? redirectUri) =>
        new(Guid.NewGuid(), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), "nonce", redirectUri);

    private static async Task<ModelTokens> SignedIn(AnthropicPlanSignInRig rig)
    {
        var attempt = Attempt(null);
        return (await rig.SignIn.Finish(attempt, rig.Pasted(attempt.State), Token))!;
    }

    private static Dictionary<string, string> Query(Uri address) =>
        address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);

    #endregion
}
