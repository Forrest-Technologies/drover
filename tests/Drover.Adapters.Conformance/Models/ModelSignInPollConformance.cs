namespace Drover.Adapters.Conformance.Models;

/// <summary>
/// What every device-code <see cref="IModelSignIn"/> must do. An adapter inherits this class and builds a rig
/// whose approval arms the provider's token answer. The suite holds the promises the poll flow rests on: Begin
/// returns a code and a verification address, Finish is null while the person has not approved, an approval
/// exchanges for a token set, a refresh repeats the Copilot token exchange with the same GitHub token, and the
/// provider's "sign-in is over" answers end it with no token in the exception.
/// </summary>
public abstract class ModelSignInPollConformance
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
        rig.SignIn.AcceptsRedirect(null).Should().BeTrue("a poll does not need a listener on the device");
    }

    [Fact]
    public async Task Begin_returns_a_user_code_and_an_https_verification_address()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt();

        var step = await rig.SignIn.Begin(attempt, Token);

        step.Wait.Should().Be(ModelSignInWait.Poll);
        step.Code.Should().NotBeNullOrWhiteSpace();
        step.Address.Scheme.Should().Be("https");
        step.Interval.Should().NotBeNull();
        step.Lifetime.Should().NotBeNull();
    }

    #endregion

    #region Finishing

    [Fact]
    public async Task Finish_returns_nothing_while_the_person_has_not_approved()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt();
        await rig.SignIn.Begin(attempt, Token);

        var tokens = await rig.SignIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().BeNull();
    }

    [Fact]
    public async Task An_approval_is_exchanged_for_a_token_set_with_a_refresh_token()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var tokens = await SignedIn(rig);

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.AccessToken.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task A_refused_consent_ends_the_attempt()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = Attempt();
        await rig.SignIn.Begin(attempt, Token);

        var finishing = () => rig.SignIn.Finish(attempt, rig.Refusal(attempt.State), Token);

        await finishing.Should().ThrowAsync<ModelSignInRefusedException>();
    }

    #endregion

    #region Refreshing

    [Fact]
    public async Task A_refresh_returns_a_new_access_token_and_keeps_the_github_token()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var first = await SignedIn(rig);

        var second = await rig.SignIn.Refresh(first, Token);
        var third = await rig.SignIn.Refresh(second, Token);

        second.AccessToken.Should().NotBe(first.AccessToken);
        third.AccessToken.Should().NotBe(second.AccessToken);
        second.RefreshToken.Should().Be(first.RefreshToken);
        third.RefreshToken.Should().Be(first.RefreshToken);
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

    /// <summary>
    /// A sign-in aimed at a provider that answers every refresh as asked. <see cref="SignInRig.Approval"/> arms the
    /// next token poll so Finish returns a token set. <paramref name="endedAnswer"/> picks the words of an ended one.
    /// </summary>
    protected abstract SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null);

    #endregion

    #region Private Methods

    private static async Task<ModelTokens> SignedIn(SignInRig rig)
    {
        var attempt = Attempt();
        await rig.SignIn.Begin(attempt, Token);
        _ = rig.Approval(attempt.State);
        return await rig.SignIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token)
            ?? throw new InvalidOperationException("The approval gave no token set.");
    }

    private static ModelSignInAttempt Attempt() => new(Guid.CreateVersion7(), Random(), Random(), Random(), null);

    private static string Random() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion
}
