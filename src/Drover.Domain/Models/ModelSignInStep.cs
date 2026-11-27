namespace Drover.Domain.Models;

/// <summary>
/// Where the person goes to approve a sign-in, the code they enter there when the flow has one, and how the answer
/// comes back.
/// </summary>
public sealed record ModelSignInStep(Uri Address, string? Code, ModelSignInWait Wait)
{
    #region Properties

    /// <summary>
    /// How long to wait between polls, when <see cref="Wait"/> is <see cref="ModelSignInWait.Poll"/>: the provider's
    /// own interval. Drover never asks sooner. Null is five seconds.
    /// </summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>How long the person has to approve, when the provider said. Null is Drover's own attempt lifetime.</summary>
    public TimeSpan? Lifetime { get; init; }

    /// <summary>
    /// What the adapter needs again to finish, such as the provider's device code. It stays on the server, in the
    /// attempt (<see cref="ModelSignInAttempt.Begun"/>), and is never sent to the device.
    /// </summary>
    public IReadOnlyDictionary<string, string> Kept { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    public override string ToString() => $"{nameof(ModelSignInStep)} {Wait}";

    #endregion
}
