namespace Drover.Domain.Models;

/// <summary>
/// One sign-in in progress, as the provider's adapter needs it. Drover makes the values: a fresh <see cref="State"/>
/// and <see cref="Nonce"/> for every attempt, and the PKCE <see cref="Verifier"/> the adapter derives its challenge
/// from. <see cref="RedirectUri"/> is where the person's device receives the browser's return, or null on a device
/// that cannot listen. It prints nothing of the verifier.
/// </summary>
public sealed record ModelSignInAttempt(Guid Id, string State, string Verifier, string Nonce, Uri? RedirectUri)
{
    #region Public Methods

    public override string ToString() => $"{nameof(ModelSignInAttempt)} {Id}";

    #endregion
}
