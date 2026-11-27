namespace Drover.Adapters.Tests;

/// <summary>
/// Copilot's browser return through the shared sign-in suite, against an in-memory GitHub that answers as a
/// registered OAuth app does.
/// </summary>
public sealed class CopilotPlanSignInConformanceTests : ModelSignInConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = ["invalid_grant", "bad_refresh_token", "401", "403"];

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var github = new CopilotAuthServer { RefreshAnswer = answer, EndedAnswer = endedAnswer };
        return new SignInRig(
            github.SignIn(),
            github.Settings.RedirectUri,
            github.BrowserApproval,
            CopilotAuthServer.BrowserRefusal,
            () => github.TokenRequests,
            endedAnswer);
    }

    #endregion
}
