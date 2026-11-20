namespace Drover.Client.Streaming;

/// <summary>
/// Whether the app is on screen, as its live streams see it. While it is hidden every stream closes its connection and
/// waits, and it connects again the moment the app is shown. The server counts an open stream as the person watching
/// the space and sends no push while one is open, so a phone that keeps a stream open in the background would never
/// be woken. A stream starts shown, and only a host that knows when it leaves the screen hides it.
/// </summary>
public sealed class StreamPresence : IDisposable
{
    #region Private Fields

    private readonly Lock gate = new();

    private CancellationTokenSource hidden = new();

    private TaskCompletionSource shown = Shown();

    #endregion

    #region Properties

    public bool IsShown
    {
        get
        {
            lock (gate)
            {
                return shown.Task.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Cancelled when the app is hidden; a stream reads its connection under it. Already cancelled while the app is
    /// hidden, so a stream that asks between being shown and connecting cannot hold a connection open in the background.
    /// </summary>
    public CancellationToken Hidden
    {
        get
        {
            lock (gate)
            {
                return shown.Task.IsCompleted ? hidden.Token : new CancellationToken(canceled: true);
            }
        }
    }

    #endregion

    #region Public Methods

    public void Hide()
    {
        CancellationTokenSource leaving;
        lock (gate)
        {
            if (!shown.Task.IsCompleted)
            {
                return;
            }

            leaving = hidden;
            hidden = new();
            shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Each stream closes its connection here. That runs outside the lock: a stream may read the state as it stops.
        // The source is not disposed, so a stream that took its token just before can still link to it.
        leaving.Cancel();
    }

    public void Show()
    {
        TaskCompletionSource arriving;
        lock (gate)
        {
            arriving = shown;
        }

        arriving.TrySetResult();
    }

    public void Dispose()
    {
        lock (gate)
        {
            hidden.Dispose();
        }
    }

    /// <summary>Returns once the app is on screen, at once when it already is.</summary>
    public Task WhenShown(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return shown.Task.WaitAsync(cancellationToken);
        }
    }

    #endregion

    #region Private Methods

    private static TaskCompletionSource Shown()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    #endregion
}
