namespace Drover.Domain.Memory;

/// <summary>Why a memory provider call failed, in the terms the rest of Drover acts on.</summary>
public enum MemoryFailure
{
    /// <summary>The key is missing, wrong, or revoked.</summary>
    InvalidKey,

    /// <summary>The provider is limiting requests. Try again after <see cref="MemoryProviderException.RetryAfter"/>.</summary>
    RetryLater,

    /// <summary>The account is out of credit or at a spend limit.</summary>
    OutOfCredit,

    /// <summary>The provider could not be reached, timed out, or failed on its side.</summary>
    Unreachable,

    /// <summary>The provider refused the request, answered in an unexpected shape, or the request was blocked.</summary>
    Rejected,
}


/// <summary>
/// A memory provider call failed. Messages are plain words and never hold a key, conversation text, or a recalled fact.
/// </summary>
public sealed class MemoryProviderException(
    MemoryFailure failure,
    string message,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    #region Properties

    public MemoryFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    #endregion

    #region Public Methods

    /// <summary>Maps an HTTP status to a failure and a safe message without repeating any key or content.</summary>
    public static MemoryProviderException FromStatus(int status, TimeSpan? retryAfter = null, Exception? inner = null) =>
        status switch
        {
            <= 0 => FromNetwork(inner),
            401 or 403 => new(
                MemoryFailure.InvalidKey,
                "The memory provider rejected the API key. Check the configured key and restart.",
                inner: inner),
            402 => new(MemoryFailure.OutOfCredit, "The memory provider says the account is out of credit.", inner: inner),
            429 => new(
                MemoryFailure.RetryLater,
                "The memory provider is limiting requests right now. Try again shortly.",
                retryAfter,
                inner),
            >= 500 => new(
                MemoryFailure.Unreachable,
                $"The memory provider failed on its side (HTTP {status}). Try again shortly.",
                inner: inner),
            _ => new(MemoryFailure.Rejected, $"The memory provider refused the request (HTTP {status}).", inner: inner),
        };

    public static MemoryProviderException FromNetwork(Exception? inner = null) =>
        new(
            MemoryFailure.Unreachable,
            "The memory provider could not be reached. Check the address and that the service is running.",
            inner: inner);

    public static MemoryProviderException FromRefusal(string message) => new(MemoryFailure.Rejected, message);

    public static MemoryProviderException FromLimit(string what) =>
        new(MemoryFailure.Rejected, $"The memory provider's {what} is larger than this server accepts, so it was not used.");

    public static MemoryProviderException FromMalformedResponse() =>
        new(MemoryFailure.Rejected, "The memory provider answered in a form Drover does not understand.");

    public static MemoryProviderException FromTimeout() =>
        new(MemoryFailure.Unreachable, "The memory provider took too long to answer. Try again shortly.");

    public static MemoryProviderException FromCircuitOpen() =>
        new(
            MemoryFailure.RetryLater,
            "Calls to the memory provider are paused for a moment after repeated failures. Try again shortly.");

    #endregion
}
