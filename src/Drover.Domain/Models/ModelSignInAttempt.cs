namespace Drover.Domain.Models;

/// <summary>
/// One sign-in in progress, as the provider's adapter needs it. Drover makes the values: a fresh <see cref="State"/>
/// and <see cref="Nonce"/> for every attempt, and the PKCE <see cref="Verifier"/> the adapter derives its challenge
/// from. <see cref="RedirectUri"/> is where the person's device receives the browser's return, or null on a device
/// that cannot listen. <see cref="Earlier"/> is what the person's own earlier sign-in of the kind in the space left
/// (its token set's <see cref="ModelTokens.Details"/>), so a provider that issued a client id to the person can be
/// asked with it again; it is empty for a first sign-in. <see cref="Begun"/> is what the adapter kept when it began the
/// attempt (<see cref="ModelSignInStep.Kept"/>), such as a device code; it stays on the server. It prints nothing of
/// the verifier.
/// </summary>
public sealed record ModelSignInAttempt(Guid Id, string State, string Verifier, string Nonce, Uri? RedirectUri)
{
    #region Properties

    public IReadOnlyDictionary<string, string> Earlier { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Begun { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    public override string ToString() => $"{nameof(ModelSignInAttempt)} {Id}";

    #endregion
}
