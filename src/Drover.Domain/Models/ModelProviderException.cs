namespace Drover.Domain.Models;

/// <summary>A provider call failed. The message is plain words for the person setting up the connection.</summary>
public sealed class ModelProviderException(
    ModelFailure failure,
    string message,
    TimeSpan? retryAfter = null,
    Exception? inner = null) : Exception(message, inner)
{
    #region Properties

    public ModelFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    #endregion

    #region Public Methods

    /// <summary>Maps an HTTP status every provider shares to a failure and a message the owner can act on.</summary>
    public static ModelProviderException FromStatus(int status, TimeSpan? retryAfter = null, Exception? inner = null) =>
        status switch
        {
            // A client library reports "no response arrived" as status 0.
            <= 0 => FromNetwork(inner),
            401 or 403 => new(
                ModelFailure.InvalidKey,
                "The provider rejected the API key. Check the key on the connection and save it again.",
                inner: inner),
            402 => new(ModelFailure.OutOfCredit, "The provider says the account is out of credit.", inner: inner),
            429 => new(
                ModelFailure.RetryLater,
                "The provider is limiting requests right now. Try again in a moment.",
                retryAfter,
                inner),
            >= 500 => new(
                ModelFailure.Unreachable,
                $"The provider failed on its side (HTTP {status}). Try again shortly.",
                inner: inner),
            _ => new(ModelFailure.Rejected, $"The provider refused the request (HTTP {status}).", inner: inner),
        };

    public static ModelProviderException FromNetwork(Exception? inner = null) =>
        new(
            ModelFailure.Unreachable,
            "The provider could not be reached. Check the address and that the server is running.",
            inner: inner);

    public static ModelProviderException FromTimeout() =>
        new(ModelFailure.Unreachable, "The provider took too long to answer. Try again shortly.");

    /// <summary>Calls to this provider are paused after repeated failures, so nothing was sent.</summary>
    public static ModelProviderException FromCircuitOpen() =>
        new(
            ModelFailure.RetryLater,
            "Calls to this provider are paused for a moment after repeated failures. Try again shortly.");

    #endregion
}
