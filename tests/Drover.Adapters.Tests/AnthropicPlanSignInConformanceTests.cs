namespace Drover.Adapters.Tests;

/// <summary>
/// The Claude plan sign-in through the shared sign-in suite, against the in-memory token address of
/// <see cref="AnthropicTokenServer"/>, written by hand from the provider's working client. The flow serves a device
/// that cannot listen with the provider's code page, so the pasting cases run too.
/// </summary>
public sealed class AnthropicPlanSignInConformanceTests : ModelSignInConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = ["invalid_grant", "refresh_token_expired", "refresh_token_reused"];

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var rig = AnthropicPlanSignInRig.Start();
        rig.Server.RefreshAnswer = answer;
        rig.Server.EndedCode = endedAnswer ?? "invalid_grant";

        return new SignInRig(
            rig.SignIn,
            rig.Options.CurrentValue.RedirectUri,
            rig.Approval,
            AnthropicPlanSignInRig.Refusal,
            () => rig.Server.Received,
            endedAnswer,
            rig.Pasted);
    }

    #endregion
}
