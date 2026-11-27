namespace Drover.Adapters.Tests;

/// <summary>
/// The ChatGPT plan's sign-in by code through the shared poll suite, against an in-memory authorization server that
/// answers as the provider's command line tool's published source shows (read 2026-11-27). Every "sign-in is over" code
/// that tool treats as permanent on a refresh is asked for in turn.
/// </summary>
public sealed class OpenAIPlanCodeSignInConformanceTests : ModelSignInPollConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } =
    [
        "invalid_grant",
        "refresh_token_expired",
        "refresh_token_invalidated",
        "refresh_token_reused",
    ];

    #endregion

    #region Protected Methods

    protected override SignInPollRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var server = new OpenAIPlanAuthServer { RefreshAnswer = answer, EndedCode = endedAnswer ?? "invalid_grant" };

        return new SignInPollRig(
            ObservedPlanSignIn.Over(server),
            () => server.Device = DeviceAnswer.Approved,
            () => server.Device = DeviceAnswer.Refused,
            () => server.CodeTokenRequests,
            endedAnswer);
    }

    #endregion
}
