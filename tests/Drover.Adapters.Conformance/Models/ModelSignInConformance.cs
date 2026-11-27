namespace Drover.Adapters.Conformance.Models;

/// <summary>How the provider's sign-in server answers a refresh in a scenario. The adapter's test turns each into its server's real answer.</summary>
public enum SignInServerAnswer
{
    Renewed,

    /// <summary>The provider says the sign-in is over, in the words <see cref="SignInRig.EndedAnswer"/> names.</summary>
    Ended,

    ServerError,

    Unreachable,
}


/// <summary>One request that reached the provider's token address: what it asked for and what it sent.</summary>
public sealed record SignInTokenRequest(string Grant, string? Code, string? Verifier, string? RefreshToken);


/// <summary>
/// A sign-in aimed at a provider that answers as asked. <see cref="Approval"/> and <see cref="Refusal"/> are what the
/// person's device receives after approving or refusing on the provider's page for an attempt with the given state.
/// <see cref="EndedAnswer"/> names which of the provider's "sign-in is over" answers the rig gives.
/// </summary>
public sealed record SignInRig(
    IModelSignIn SignIn,
    Uri RedirectUri,
    Func<string, ModelSignInReturn> Approval,
    Func<string, ModelSignInReturn> Refusal,
    Func<IReadOnlyList<SignInTokenRequest>> Received,
    string? EndedAnswer = null);


/// <summary>
/// What every <see cref="IModelSignIn"/> must do. An adapter inherits this class and builds a rig for each answer. The
/// suite holds the promises the design rests on: the browser flow is PKCE with Drover's fresh state, nothing is
/// exchanged for an answer that is not the attempt's approval, a refresh token is never sent twice, the provider's
/// "sign-in is over" answers and only those end a sign-in, and no token is ever put in an exception's message.
/// </summary>
/// <remarks>
/// This first suite covers the browser flow whose answer the person's device receives (<see cref="ModelSignInWait.Return"/>).
/// The flows by pasted code and by polling add their cases with the waits that name them.
/// </remarks>
public abstract class ModelSignInConformance
{
    #region Properties

    /// <summary>Every answer the provider gives for "the sign-in is over". The suite asks for each in turn.</summary>
    protected abstract IReadOnlyList<string> EndedAnswers { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Starting

    [Fact]
    public void The_sign_in_has_a_kind_and_a_name()
    {
        var rig = Rig(SignInServerAnswer.Renewed);

        rig.SignIn.Kind.Should().NotBeNullOrWhiteSpace();
        rig.SignIn.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_configured_return_address_is_accepted_on_any_port_and_another_address_is_not()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var otherPort = new UriBuilder(rig.RedirectUri) { Port = rig.RedirectUri.Port == 50123 ? 50124 : 50123 }.Uri;

        rig.SignIn.AcceptsRedirect(rig.RedirectUri).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(otherPort).Should().BeTrue();
        rig.SignIn.AcceptsRedirect(new UriBuilder(rig.RedirectUri) { Path = "/elsewhere" }.Uri).Should().BeFalse();
        rig.SignIn.AcceptsRedirect(new UriBuilder(rig.RedirectUri) { Host = "attacker.example.test" }.Uri).Should().BeFalse();
    }

    [Fact]
    public async Task The_address_carries_a_pkce_challenge_and_the_attempts_own_state()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var first = Attempt(rig);
        var second = Attempt(rig);

        var firstStep = await rig.SignIn.Begin(first, Token);
        var secondStep = await rig.SignIn.Begin(second, Token);

        firstStep.Address.Scheme.Should().Be("https");
        firstStep.Wait.Should().Be(ModelSignInWait.Return);
        var query = Query(firstStep.Address);
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().Be(Challenge(first.Verifier));
        query["state"].Should().Be(first.State);
        Query(secondStep.Address)["state"].Should().Be(second.State).And.NotBe(first.State);
    }

    #endregion

    #region Finishing

    [Fact]
    public async Task An_approval_is_exchanged_once_with_the_verifier_for_a_token_set()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt(rig);
        await rig.SignIn.Begin(attempt, Token);

        var tokens = await rig.SignIn.Finish(attempt, rig.Approval(attempt.State), Token);

        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        var exchange = rig.Received().Should().ContainSingle().Subject;
        exchange.Grant.Should().Be("authorization_code");
        exchange.Verifier.Should().Be(attempt.Verifier);
    }

    [Fact]
    public async Task A_return_with_another_attempts_state_is_refused_and_nothing_is_exchanged()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt(rig);
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, rig.Approval(Random()), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        rig.Received().Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_consent_ends_the_attempt_and_nothing_is_exchanged()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt(rig);
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, rig.Refusal(attempt.State), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        rig.Received().Should().BeEmpty();
    }

    [Fact]
    public async Task A_return_that_is_not_a_query_string_is_refused_and_nothing_is_exchanged()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt(rig);
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
        rig.Received().Should().BeEmpty();
    }

    #endregion

    #region Refreshing

    [Fact]
    public async Task A_refresh_returns_a_new_token_set_and_never_sends_a_refresh_token_twice()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var first = await SignedIn(rig);

        var second = await rig.SignIn.Refresh(first, Token);
        var third = await rig.SignIn.Refresh(second, Token);

        second.AccessToken.Should().NotBe(first.AccessToken);
        third.AccessToken.Should().NotBe(second.AccessToken);
        List<string?> sent = [.. rig.Received().Where(request => request.Grant == "refresh_token").Select(request => request.RefreshToken)];
        sent.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.NotContainNulls();
    }

    [Fact]
    public async Task Every_answer_that_the_sign_in_is_over_ends_it()
    {
        foreach (var answer in EndedAnswers)
        {
            var rig = Rig(SignInServerAnswer.Ended, answer);
            var tokens = await SignedIn(rig);

            var refreshing = () => rig.SignIn.Refresh(tokens, Token);

            (await refreshing.Should().ThrowAsync<SignInEndedException>($"{answer} says the sign-in is over")).Which.Message
                .Should().NotContain(tokens.AccessToken).And.NotContain(tokens.RefreshToken ?? tokens.AccessToken);
        }
    }

    [Theory]
    [InlineData(SignInServerAnswer.ServerError)]
    [InlineData(SignInServerAnswer.Unreachable)]
    public async Task A_failure_that_may_pass_does_not_end_the_sign_in_and_names_no_token(SignInServerAnswer answer)
    {
        var rig = Rig(answer);
        var tokens = await SignedIn(rig);

        var refreshing = () => rig.SignIn.Refresh(tokens, Token);

        var failure = (await refreshing.Should().ThrowAsync<Exception>()).Which;
        failure.Should().NotBeOfType<SignInEndedException>();
        failure.ToString().Should().NotContain(tokens.AccessToken).And.NotContain(tokens.RefreshToken ?? tokens.AccessToken);
    }

    [Fact]
    public async Task A_token_set_prints_nothing_of_its_tokens()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var tokens = await SignedIn(rig);

        tokens.ToString().Should().NotContain(tokens.AccessToken);
        $"{tokens}".Should().NotContain(tokens.RefreshToken ?? tokens.AccessToken);
    }

    [Fact]
    public async Task Signing_out_is_accepted()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var tokens = await SignedIn(rig);

        var revoking = () => rig.SignIn.Revoke(tokens, Token);

        await revoking.Should().NotThrowAsync();
    }

    #endregion

    #region Protected Methods

    /// <summary>A sign-in aimed at a provider that answers every refresh as asked; <paramref name="endedAnswer"/> picks the words of an ended one.</summary>
    protected abstract SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null);

    #endregion

    #region Private Methods

    private static async Task<ModelTokens> SignedIn(SignInRig rig)
    {
        var attempt = Attempt(rig);
        await rig.SignIn.Begin(attempt, Token);
        return await rig.SignIn.Finish(attempt, rig.Approval(attempt.State), Token)
            ?? throw new InvalidOperationException("The approval gave no token set.");
    }

    private static ModelSignInAttempt Attempt(SignInRig rig) => new(Guid.CreateVersion7(), Random(), Random(), Random(), rig.RedirectUri);

    private static string Random() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> Query(Uri address) =>
        address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);

    #endregion
}
