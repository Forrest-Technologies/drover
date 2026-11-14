namespace Drover.Domain.Sandboxes;

/// <summary>
/// A sandbox provider call failed. The message is plain words for the person running the server. It never holds an
/// access token, a key, or text the sandbox produced, and there is no inner exception that could.
/// </summary>
public sealed class SandboxProviderException(SandboxFailure failure, string message, TimeSpan? retryAfter = null)
    : Exception(message)
{
    #region Properties

    public SandboxFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    #endregion
}
