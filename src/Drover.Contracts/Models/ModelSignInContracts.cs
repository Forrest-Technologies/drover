namespace Drover.Contracts.Models;

/// <summary>
/// Starts a person's own sign-in to a model provider. <see cref="RedirectUri"/> is the address on the person's device
/// that receives the browser's return, or null on a device that cannot listen, which signs in by code.
/// </summary>
public sealed record StartModelSignInRequest(string? Kind, string? RedirectUri);

/// <summary>
/// Where the person goes to approve, how the answer comes back (<see cref="ModelSignInWaits"/>), and the short code they
/// enter there when the flow has one. <see cref="Code"/> is shown on the device; it is not a secret.
/// </summary>
public sealed record ModelSignInStarted(Guid AttemptId, string Address, string Wait, string? Code = null);

/// <summary>
/// Input only: the query string the person's device received from the provider's page, or what the person pasted from
/// it when the wait is <see cref="ModelSignInWaits.Paste"/>. It holds a one-time code.
/// </summary>
public sealed record FinishModelSignInRequest(string? Query)
{
    #region Private Fields

    /// <summary>The longest return the server takes, in characters: 8 KB, as a browser's request line is bounded.</summary>
    public const int MostLength = 8192;

    #endregion

    #region Public Methods

    public override string ToString() => nameof(FinishModelSignInRequest);

    #endregion
}

/// <summary>
/// Where a sign-in stands (<see cref="ModelSignInStatuses"/>), the connection once it is done, and Drover's own
/// sentence for why it ended without one when there is one.
/// </summary>
public sealed record ModelSignInProgress(string Status, ModelConnectionSummary? Connection, string? Problem = null);

/// <summary>
/// The caller's own sign-in to a connection each person signs in to: <c>none</c>, <c>active</c>, <c>needs_sign_in</c>
/// (the provider ended it), or <c>signed_out</c> (the person signed out, or signed in without allowing plan use, and
/// only what the provider registered for them is kept), and what the provider says the caller's account is called.
/// Never anyone else's.
/// </summary>
public sealed record ModelSignInSummary(string Status, string? AccountLabel);

/// <summary>The words the server sends about model sign-ins.</summary>
public static class ModelSignInStatuses
{
    #region Attempts

    public const string Waiting = "waiting";

    public const string Done = "done";

    public const string Refused = "refused";

    public const string Expired = "expired";

    #endregion

    #region Sign-ins

    public const string None = "none";

    public const string Active = "active";

    public const string NeedsSignIn = "needs_sign_in";

    public const string SignedOut = "signed_out";

    #endregion
}

/// <summary>How the answer to a sign-in comes back.</summary>
public static class ModelSignInWaits
{
    #region Waits

    /// <summary>The person's device receives the browser's return and posts its query string to finish.</summary>
    public const string Return = "return";

    /// <summary>The provider's page shows a code; the person pastes it and the device posts it to finish.</summary>
    public const string Paste = "paste";

    /// <summary>
    /// The person enters <see cref="ModelSignInStarted.Code"/> on the provider's page; the server finishes by itself and
    /// the device reads progress until it is done.
    /// </summary>
    public const string Poll = "poll";

    #endregion
}
