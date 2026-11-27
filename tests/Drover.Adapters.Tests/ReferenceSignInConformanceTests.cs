namespace Drover.Adapters.Tests;

/// <summary>
/// The model sign-in suite run against the in-memory reference provider the engine and API tests sign in with, so
/// the suite is known to pass for a provider that keeps every promise before a real adapter is held to it, and the
/// fake those tests rely on is held to the same promises.
/// </summary>
public sealed class ReferenceSignInConformanceTests : ModelSignInConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = ["invalid_grant"];

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var provider = new FakeModelSignIn
        {
            RefreshAnswer = answer switch
            {
                SignInServerAnswer.Ended => FakeRefreshAnswer.Ended,
                SignInServerAnswer.ServerError => FakeRefreshAnswer.ServerError,
                SignInServerAnswer.Unreachable => FakeRefreshAnswer.Unreachable,
                _ => FakeRefreshAnswer.Renewed,
            },
        };

        return new SignInRig(
            provider,
            FakeModelSignIn.RedirectUri,
            provider.Approve,
            FakeModelSignIn.Refuse,
            () => [.. provider.Received.Select(request => new SignInTokenRequest(request.Grant, request.Code, request.Verifier, request.RefreshToken))],
            endedAnswer);
    }

    #endregion
}
