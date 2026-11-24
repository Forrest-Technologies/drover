namespace Drover.Ui;

/// <summary>
/// Reading a screen's token once the screen was left.
/// </summary>
public static class CancellationSourceExtensions
{
    #region Public Methods

    /// <summary>
    /// The token of a source that ends when a person leaves a screen. A source is disposed as the screen closes, and
    /// asking a disposed source for its token throws, so a request that ends after the screen was left would raise
    /// that failure on the interface thread. A left screen gets a token that is already cancelled instead: what it
    /// starts ends quietly. A source is only disposed after it was cancelled, so asking whether it was cancelled first
    /// never reaches a disposed one. Ask once before the first await, and use the answer for the rest of the call.
    /// </summary>
    public static CancellationToken LeavingToken(this CancellationTokenSource source) =>
        source.IsCancellationRequested ? new(canceled: true) : source.Token;

    #endregion
}
