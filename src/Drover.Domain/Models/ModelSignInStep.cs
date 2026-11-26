namespace Drover.Domain.Models;

/// <summary>
/// Where the person goes to approve a sign-in, the code they enter there when the flow has one, and how the answer
/// comes back.
/// </summary>
public sealed record ModelSignInStep(Uri Address, string? Code, ModelSignInWait Wait)
{
    #region Properties

    /// <summary>How long to wait between polls, when <see cref="Wait"/> is <see cref="ModelSignInWait.Poll"/>.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>How long the person has to approve, when the provider said. Null uses Drover's attempt lifetime.</summary>
    public TimeSpan? Lifetime { get; init; }

    #endregion
}
