namespace Drover.Adapters.Tests;

/// <summary>
/// OpenRouter's sign-in through the shared suite. Its exchange answers a key with no refresh token and no end, so the
/// rotation and ended-answer cases do not apply (<see cref="RefreshesTokens"/>); the rest of the suite does.
/// </summary>
public sealed class OpenRouterSignInConformanceTests : ModelSignInConformance
{
    #region Properties

    protected override IReadOnlyList<string> EndedAnswers { get; } = [];

    protected override bool RefreshesTokens => false;

    #endregion

    #region Protected Methods

    protected override SignInRig Rig(SignInServerAnswer answer, string? endedAnswer = null)
    {
        var rig = new OpenRouterSignInRig();

        return new SignInRig(
            rig.SignIn,
            rig.Options.CurrentValue.RedirectUri,
            state => new ModelSignInReturn(rig.Provider.Approve(state)),
            state => new ModelSignInReturn($"error=access_denied&state={Uri.EscapeDataString(state)}"),
            () => [.. rig.Provider.Requests.Select(request => new SignInTokenRequest(
                "authorization_code",
                request.Json.GetProperty("code").GetString(),
                request.Json.GetProperty("code_verifier").GetString(),
                null))],
            endedAnswer);
    }

    #endregion
}
