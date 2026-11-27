namespace Drover.Adapters.Tests;

/// <summary>
/// Copilot's device-code poll through the shared poll suite, against the same registered GitHub app the browser
/// return uses.
/// </summary>
public sealed class CopilotPlanPollSignInConformanceTests : ModelSignInPollConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = ["invalid_grant", "bad_refresh_token", "401", "403"];

    #endregion

    #region Protected Methods

    protected override SignInPollRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var github = new CopilotAuthServer { RefreshAnswer = answer, EndedAnswer = endedAnswer };
        return new SignInPollRig(
            github.SignIn(),
            github.Approve,
            github.Refuse,
            () => github.TokenRequests,
            endedAnswer);
    }

    #endregion
}
