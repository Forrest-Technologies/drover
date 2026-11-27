namespace Drover.Contracts.Models;

/// <summary>
/// Starts a person's own sign-in to a model provider. <see cref="RedirectUri"/> is the address on the person's device
/// that receives the browser's return.
/// </summary>
public sealed record StartModelSignInRequest(string? Kind, string? RedirectUri);

/// <summary>Where the person goes to approve, and how the answer comes back (<see cref="ModelSignInWaits"/>).</summary>
public sealed record ModelSignInStarted(Guid AttemptId, string Address, string Wait);

/// <summary>Input only: the query string the person's device received from the provider's page. It holds a one-time code.</summary>
public sealed record FinishModelSignInRequest(string? Query)
{
    public override string ToString() => nameof(FinishModelSignInRequest);
}

/// <summary>Where a sign-in stands (<see cref="ModelSignInStatuses"/>), and the connection once it is done.</summary>
public sealed record ModelSignInProgress(string Status, ModelConnectionSummary? Connection);

/// <summary>
/// The caller's own sign-in to a connection each person signs in to: <c>none</c>, <c>active</c>, or
/// <c>needs_sign_in</c>, and what the provider says the caller's account is called. Never anyone else's.
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

    #endregion
}

/// <summary>How the answer to a sign-in comes back.</summary>
public static class ModelSignInWaits
{
    #region Waits

    /// <summary>The person's device receives the browser's return and posts its query string to finish.</summary>
    public const string Return = "return";

    #endregion
}
