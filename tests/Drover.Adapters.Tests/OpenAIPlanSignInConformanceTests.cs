namespace Drover.Adapters.Tests;

/// <summary>
/// The ChatGPT plan sign-in through the shared sign-in suite, against an in-memory authorization server that answers
/// as the provider's pages say it does. Every "sign-in is over" code the provider lists for a refresh is asked for in
/// turn (https://developers.openai.com/siwc/llms-full.txt, "Refresh errors", read 2026-11-27).
/// </summary>
public sealed class OpenAIPlanSignInConformanceTests : ModelSignInConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } =
    [
        "invalid_grant",
        "invalid_refresh_token",
        "token_expired",
        "refresh_token_expired",
        "refresh_token_invalidated",
        "refresh_token_reused",
    ];

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var server = new OpenAIPlanAuthServer { RefreshAnswer = answer, EndedCode = endedAnswer ?? "invalid_grant" };

        return new SignInRig(
            ObservedPlanSignIn.Over(server),
            server.Settings.RedirectUri,
            state => server.Approve(state),
            OpenAIPlanAuthServer.Refuse,
            () => server.TokenRequests,
            endedAnswer);
    }

    #endregion
}
