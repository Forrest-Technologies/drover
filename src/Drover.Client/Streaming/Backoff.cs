namespace Drover.Client.Streaming;

/// <summary>How long to wait before connecting again: half a second, doubling to ten, and back to the start after any success.</summary>
public sealed class Backoff
{
    #region Private Fields

    private static readonly TimeSpan first = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan longest = TimeSpan.FromSeconds(10);

    private TimeSpan next = first;

    #endregion

    #region Public Methods

    public void Reset() => next = first;

    public TimeSpan Next()
    {
        var wait = next;
        next = next * 2 > longest ? longest : next * 2;
        return wait;
    }

    #endregion
}
