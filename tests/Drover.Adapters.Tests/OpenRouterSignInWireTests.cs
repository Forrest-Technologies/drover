namespace Drover.Adapters.Tests;

/// <summary>
/// What the OpenRouter sign-in puts on the wire and how it reads the answers, from the provider's page
/// (https://openrouter.ai/docs/use-cases/oauth-pkce, read 2026-11-27): the address, the key exchange, which return
/// addresses are taken, what a refusal and an outage look like, that a setting changes the next request, and that a
/// run calls the provider with the person's key.
/// </summary>
public sealed class OpenRouterSignInWireTests
{
    #region Private Fields

    private static readonly Uri desktopReturn = new("http://127.0.0.1:50321/auth/callback");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Sign-In Address

    [Fact]
    public async Task The_address_carries_the_callback_the_challenge_and_the_state_and_no_client_id()
    {
        var rig = new OpenRouterSignInRig();
        var attempt = Attempt(desktopReturn);

        var step = await rig.SignIn.Begin(attempt, Token);

        step.Address.GetLeftPart(UriPartial.Path).Should().Be("https://openrouter.ai/auth");
        var query = FakeOpenRouter.Read(step.Address.Query);
        query["callback_url"].Should().Be("http://localhost:50321/auth/callback", "the page documents localhost callbacks, so the loopback address is spelled that way");
        query["code_challenge_method"].Should().Be("S256");
        query["state"].Should().Be(attempt.State);
        query.Keys.Should().NotContain(["client_id", "client_secret"]);
        step.Wait.Should().Be(ModelSignInWait.Return);
        step.Code.Should().BeNull();
    }

    [Fact]
    public async Task A_return_to_the_servers_own_https_address_is_sent_as_it_is()
    {
        var rig = new OpenRouterSignInRig();
        var own = new Uri($"https://drover.example.test/api/model-sign-in/return/{Guid.NewGuid()}");

        var step = await rig.SignIn.Begin(Attempt(own), Token);

        FakeOpenRouter.Read(step.Address.Query)["callback_url"].Should().Be(own.AbsoluteUri);
        rig.SignIn.CanReturnToServer.Should().BeTrue();
        rig.SignIn.AcceptsRedirect(own).Should().BeFalse("a device may not name an address that is not its own listener");
        rig.SignIn.AcceptsRedirect(null).Should().BeFalse();
    }

    [Fact]
    public async Task A_head_with_no_return_address_cannot_start_a_sign_in_here()
    {
        var rig = new OpenRouterSignInRig();

        var beginning = () => rig.SignIn.Begin(Attempt(null), Token);

        await beginning.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Key Exchange

    [Fact]
    public async Task The_exchange_posts_the_code_the_verifier_and_the_method_as_json_and_keeps_the_key_with_no_end()
    {
        var rig = new OpenRouterSignInRig();
        var attempt = Attempt(desktopReturn);
        await rig.SignIn.Begin(attempt, Token);

        var tokens = await rig.SignIn.Finish(attempt, new ModelSignInReturn(rig.Provider.Approve(attempt.State)), Token);

        var sent = rig.Provider.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.Should().Be(new Uri("https://openrouter.ai/api/v1/auth/keys"));
        sent.Json.GetProperty("code_verifier").GetString().Should().Be(attempt.Verifier);
        sent.Json.GetProperty("code_challenge_method").GetString().Should().Be("S256");
        sent.Json.GetProperty("code").GetString().Should().StartWith("code-");
        tokens!.AccessToken.Should().Be(rig.Provider.Issued.Single());
        tokens.RefreshToken.Should().BeNull();
        tokens.ExpiresAt.Should().BeNull("a key has no end, so the hourly renewal never takes it up");
        tokens.AccountLabel.Should().BeNull();
        (await rig.SignIn.Refresh(tokens, Token)).Should().BeSameAs(tokens);
        rig.Provider.Requests.Should().ContainSingle("a refresh asks the provider nothing");
    }

    [Fact]
    public async Task A_code_that_was_already_used_is_refused()
    {
        var rig = new OpenRouterSignInRig();
        var attempt = Attempt(desktopReturn);
        await rig.SignIn.Begin(attempt, Token);
        var arrived = new ModelSignInReturn(rig.Provider.Approve(attempt.State));
        await rig.SignIn.Finish(attempt, arrived, Token);

        var again = () => rig.SignIn.Finish(attempt, arrived, Token);

        await again.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_code_the_provider_will_not_exchange_ends_the_attempt_without_a_key(HttpStatusCode status)
    {
        var rig = new OpenRouterSignInRig();
        rig.Provider.ExchangeStatus = status;
        var attempt = Attempt(desktopReturn);
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, new ModelSignInReturn(rig.Provider.Approve(attempt.State)), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_provider_that_is_down_or_busy_is_not_a_refusal(HttpStatusCode status)
    {
        var rig = new OpenRouterSignInRig();
        rig.Provider.ExchangeStatus = status;
        var attempt = Attempt(desktopReturn);
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, new ModelSignInReturn(rig.Provider.Approve(attempt.State)), Token);

        (await finishing.Should().ThrowAsync<Exception>()).Which.Should().BeOfType<ModelProviderException>();
    }

    [Fact]
    public async Task A_network_failure_and_an_answer_with_no_key_are_provider_failures_that_name_nothing()
    {
        var down = new OpenRouterSignInRig();
        down.Provider.Unreachable = true;
        var empty = new OpenRouterSignInRig();
        empty.Provider.ExchangeBody = """{"user_id":"user_1"}""";

        foreach (var rig in new[] { down, empty })
        {
            var attempt = Attempt(desktopReturn);
            await rig.SignIn.Begin(attempt, Token);
            var arrived = new ModelSignInReturn(rig.Provider.Approve(attempt.State));

            var finishing = () => rig.SignIn.Finish(attempt, arrived, Token);

            var failure = (await finishing.Should().ThrowAsync<ModelProviderException>()).Which;
            failure.ToString().Should().NotContain(attempt.Verifier).And.NotContain(arrived.Text);
        }
    }

    #endregion

    #region Return Addresses

    [Fact]
    public void The_return_address_may_be_loopback_by_either_name_on_any_port_and_nothing_else()
    {
        var rig = new OpenRouterSignInRig();

        rig.SignIn.AcceptsRedirect(desktopReturn).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new Uri("http://localhost:1/auth/callback")).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new Uri("https://localhost:1455/auth/callback")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("http://localhost:1455/auth/callback?x=1")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("http://evil.example.test:1455/auth/callback")).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new Uri("http://localhost:1455/other")).Should().BeFalse();
    }

    #endregion

    #region Settings

    [Fact]
    public async Task Changing_an_address_or_the_label_changes_the_next_request_with_no_rebuild()
    {
        var rig = new OpenRouterSignInRig();
        var before = await rig.SignIn.Begin(Attempt(desktopReturn), Token);
        rig.Options.CurrentValue = new OpenRouterSignInOptions
        {
            AuthorizeAddress = new Uri("https://auth.example.test/start"),
            TokenAddress = new Uri("https://auth.example.test/exchange"),
            KeyLabel = "Elsewhere",
        };

        var attempt = Attempt(desktopReturn);
        var after = await rig.SignIn.Begin(attempt, Token);
        await rig.SignIn.Finish(attempt, new ModelSignInReturn(rig.Provider.Approve(attempt.State)), Token);

        before.Address.Host.Should().Be("openrouter.ai");
        after.Address.GetLeftPart(UriPartial.Path).Should().Be("https://auth.example.test/start");
        FakeOpenRouter.Read(after.Address.Query)["key_label"].Should().Be("Elsewhere");
        rig.Provider.Requests.Single().Uri.Should().Be(new Uri("https://auth.example.test/exchange"));
    }

    #endregion

    #region Private Methods

    private static ModelSignInAttempt Attempt(Uri? redirect) =>
        new(Guid.NewGuid(), Random(), Random(), Random(), redirect);

    private static string Random() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion
}
