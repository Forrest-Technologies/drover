namespace Drover.Domain.Models;

/// <summary>
/// The provider asked Drover to poll less often. The attempt goes on: the next poll waits five seconds longer than the
/// last wait, or <see cref="Interval"/> when the provider named a new interval and it is longer still.
/// </summary>
public sealed class ModelSignInSlowDownException(TimeSpan? interval = null) : Exception("The provider asked for slower polling.")
{
    #region Properties

    public TimeSpan? Interval { get; } = interval;

    #endregion
}
