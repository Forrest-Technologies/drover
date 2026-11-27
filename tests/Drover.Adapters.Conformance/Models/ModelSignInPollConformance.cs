namespace Drover.Adapters.Conformance.Models;

/// <summary>
/// A sign-in by code aimed at a provider that answers as asked. <see cref="Approve"/> and <see cref="Refuse"/> are the
/// person approving or refusing on the provider's page; <see cref="Received"/> the requests that reached its token
/// address; <see cref="EndedAnswer"/> which of its "sign-in is over" answers the rig gives.
/// </summary>
public sealed record SignInPollRig(
    IModelSignIn SignIn,
    Action Approve,
    Action Refuse,
    Func<IReadOnlyList<SignInTokenRequest>> Received,
    string? EndedAnswer = null);


/// <summary>
/// What every sign-in by a code the server polls for (<see cref="ModelSignInWait.Poll"/>) must do, for a device that
/// cannot listen. An adapter inherits this class and builds a rig for each answer. The suite holds the promises the
/// poll rests on: the start names a code, an https page, an interval and a lifetime of at most fifteen minutes; a poll
/// is null while the person has not approved; an approval is exchanged for a token set once; a refusal ends the
/// attempt; and refreshing keeps the rules of the browser flow: a refresh token is never sent twice, the provider's
/// "sign-in is over" answers and only those end it, and no token is put in an exception's message.
/// </summary>
/// <remarks>Built on the poll suite of the GitHub Copilot sign-in work (#1036), made generic for every kind.</remarks>
public abstract class ModelSignInPollConformance
{
    #region Properties

    /// <summary>Every answer the provider gives for "the sign-in is over". The suite asks for each in turn.</summary>
    protected abstract IReadOnlyList<string> EndedAnswers { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Starting

    [Fact]
    public void A_device_that_cannot_listen_is_accepted()
    {
        var rig = Rig(SignInServerAnswer.Renewed);

        rig.SignIn.AcceptsRedirect(null).Should().BeTrue("a poll needs no listener on the device");
    }

    [Fact]
    public async Task The_start_names_a_code_an_https_page_an_interval_and_a_lifetime()
    {
        var rig = Rig(SignInServerAnswer.Renewed);

        var step = await rig.SignIn.Begin(Attempt(), Token);

        step.Wait.Should().Be(ModelSignInWait.Poll);
        step.Code.Should().NotBeNullOrWhiteSpace();
        step.Address.Scheme.Should().Be(Uri.UriSchemeHttps);
        (step.Interval is null || step.Interval > TimeSpan.Zero).Should().BeTrue("an interval the provider names is a wait, and none is five seconds");
        step.Lifetime.Should().NotBeNull();
        step.Lifetime!.Value.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    #endregion

    #region Finishing

    [Fact]
    public async Task A_poll_is_nothing_while_the_person_has_not_approved()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = await Begun(rig);

        var tokens = await rig.SignIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().BeNull();
        rig.Received().Should().BeEmpty("nothing is exchanged before the person approves");
    }

    [Fact]
    public async Task An_approval_is_exchanged_once_for_a_token_set()
    {
        var rig = Rig(SignInServerAnswer.Renewed);

        var tokens = await SignedIn(rig);

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        rig.Received().Should().ContainSingle();
    }

    [Fact]
    public async Task A_refusal_on_the_providers_page_ends_the_attempt_and_nothing_is_exchanged()
    {
        var rig = Rig(SignInServerAnswer.Renewed);
        var attempt = await Begun(rig);
        rig.Refuse();

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
        var sent = rig.Received().Select(request => request.RefreshToken).OfType<string>().ToList();
        sent.Should().OnlyHaveUniqueItems();
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

    /// <summary>A sign-in by code aimed at a provider that answers every refresh as <paramref name="answer"/> says.</summary>
    protected abstract SignInPollRig Rig(SignInServerAnswer answer, string? endedAnswer = null);

    #endregion

    #region Private Methods

    /// <summary>An attempt begun as the server begins one: with what the adapter kept for its finish.</summary>
    private static async Task<ModelSignInAttempt> Begun(SignInPollRig rig)
    {
        var attempt = Attempt();
        var step = await rig.SignIn.Begin(attempt, Token);
        return attempt with { Begun = step.Kept };
    }

    private static async Task<ModelTokens> SignedIn(SignInPollRig rig)
    {
        var attempt = await Begun(rig);
        rig.Approve();
        return await rig.SignIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token)
            ?? throw new InvalidOperationException("The approval gave no token set.");
    }

    private static ModelSignInAttempt Attempt() => new(Guid.NewGuid(), Random(), Random(), Random(), null);

    private static string Random() => System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    #endregion
}
