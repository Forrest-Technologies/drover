namespace Drover.Adapters.Tests;

/// <summary>
/// Copilot-specific GitHub OAuth behaviour the shared suites do not cover: missing operator settings, slow-down,
/// a malformed verification address, and that the GitHub user token is the chat bearer with no internal exchange.
/// </summary>
public sealed class CopilotPlanSignInTests
{
    #region Private Fields

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Settings

    [Fact]
    public void A_copilot_plan_is_a_personal_plan() =>
        new CopilotAuthServer().SignIn().IsPersonalPlan.Should().BeTrue();

    [Fact]
    public async Task Begin_without_a_client_id_names_the_setting_to_set()
    {
        var signIn = new CopilotAuthServer().SignIn(new CopilotPlanOptions());

        var beginning = () => signIn.Begin(ReturnAttempt(), Token);

        var failure = await beginning.Should().ThrowAsync<ModelProviderException>();
        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
        failure.Which.Message.Should().Contain(CopilotPlanOptions.Section).And.Contain(nameof(CopilotPlanOptions.ClientId));
    }

    [Fact]
    public async Task A_browser_return_without_a_client_secret_names_the_setting_to_set()
    {
        var github = new CopilotAuthServer();
        var signIn = github.SignIn(new CopilotPlanOptions { ClientId = CopilotAuthServer.TestClientId });
        var attempt = ReturnAttempt();
        await signIn.Begin(attempt, Token);

        var finishing = () => signIn.Finish(attempt, github.BrowserApproval(attempt.State), Token);

        var failure = await finishing.Should().ThrowAsync<ModelProviderException>();
        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
        failure.Which.Message.Should().Contain(nameof(CopilotPlanOptions.ClientSecret));
    }

    [Fact]
    public async Task Changing_the_client_id_changes_what_begin_sends()
    {
        var github = new CopilotAuthServer();
        var options = new CopilotPlanOptions { ClientId = "Iv1.other-client", ClientSecret = CopilotAuthServer.TestClientSecret };
        var signIn = github.SignIn(options);

        await signIn.Begin(DeviceAttempt(), Token);

        github.Requests[0].Body.Should().Contain("client_id=Iv1.other-client");
    }

    #endregion

    #region Device Flow

    [Fact]
    public async Task Slow_down_asks_Drover_to_wait_longer()
    {
        var github = new CopilotAuthServer { TokenError = "slow_down", TokenInterval = 12 };
        var signIn = github.SignIn();
        var attempt = await Begun(signIn);

        var finishing = () => signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        var failure = await finishing.Should().ThrowAsync<ModelSignInSlowDownException>();
        failure.Which.Interval.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task An_expired_device_code_refuses_the_attempt()
    {
        var github = new CopilotAuthServer { TokenError = "expired_token" };
        var signIn = github.SignIn();
        var attempt = await Begun(signIn);

        var finishing = () => signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    [Fact]
    public async Task A_verification_address_that_is_not_http_is_refused()
    {
        var github = new CopilotAuthServer { VerificationUri = "javascript:alert(1)" };
        var signIn = github.SignIn();

        var beginning = () => signIn.Begin(DeviceAttempt(), Token);

        await beginning.Should().ThrowAsync<ModelProviderException>();
    }

    #endregion

    #region Tokens

    [Fact]
    public async Task The_github_user_token_is_the_access_token_and_nothing_calls_an_internal_exchange()
    {
        var github = new CopilotAuthServer();
        var signIn = github.SignIn();
        var attempt = await Begun(signIn);
        github.Approve();

        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().StartWith("gho_");
        tokens.AccountLabel.Should().Be("sam");
        github.Requests.Should().NotContain(request => request.Uri.AbsolutePath.Contains("copilot_internal", StringComparison.Ordinal));
        github.Requests.Should().OnlyContain(request =>
            !request.Headers.ContainsKey("Editor-Version")
            && !request.Headers.ContainsKey("Copilot-Integration-Id"));
        github.Requests.Should().OnlyContain(request =>
            request.Headers.GetValueOrDefault("User-Agent") == CopilotPlanOptions.UserAgent);
    }

    [Fact]
    public async Task A_token_with_no_refresh_token_is_handed_back_on_refresh()
    {
        var github = new CopilotAuthServer();
        var signIn = github.SignIn();
        var current = new ModelTokens { AccessToken = "gho_only", RefreshToken = null };

        var refreshed = await signIn.Refresh(current, Token);

        refreshed.Should().BeSameAs(current);
        github.TokenRequests.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static ModelSignInAttempt ReturnAttempt() =>
        new(Guid.CreateVersion7(), "state", "verifier", "nonce", new Uri("http://127.0.0.1:1455/auth/callback"));

    private static ModelSignInAttempt DeviceAttempt() =>
        new(Guid.CreateVersion7(), "state", "verifier", "nonce", null);

    private static async Task<ModelSignInAttempt> Begun(CopilotPlanSignIn signIn)
    {
        var attempt = DeviceAttempt();
        var step = await signIn.Begin(attempt, Token);
        return attempt with { Begun = step.Kept };
    }

    #endregion
}
