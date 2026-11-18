namespace Drover.Presentation.Tests.Support;

/// <summary>
/// One thread that stands in for the interface's thread. Work started on it comes back to it after every wait, as
/// it does in the app, so a view model under test is only ever touched from one thread.
/// </summary>
public sealed class UiLoop : SynchronizationContext, IUiThread, IDisposable
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan pause = TimeSpan.FromMilliseconds(15);

    private readonly BlockingCollection<Action> queue = [];

    private readonly Thread thread;

    #endregion

    #region Constructors

    public UiLoop()
    {
        thread = new(Pump) { IsBackground = true, Name = nameof(UiLoop) };
        thread.Start();
    }

    #endregion

    #region Public Methods

    public void Dispose() => queue.CompleteAdding();

    public override void Post(SendOrPostCallback d, object? state) => Enqueue(() => d(state));

    void IUiThread.Post(Action work)
    {
        if (Thread.CurrentThread == thread)
        {
            work();
        }
        else
        {
            Enqueue(work);
        }
    }

    /// <summary>Starts work on the loop and waits until all of it is done.</summary>
    public Task Run(Func<Task> work)
    {
        var started = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() => started.SetResult(Start(work)));
        return started.Task.Unwrap();
    }

    public Task Run(Action work) => Run(() =>
    {
        work();
        return Task.CompletedTask;
    });

    /// <summary>Reads something from the view model, on the loop.</summary>
    public async Task<T> Read<T>(Func<T> read)
    {
        T value = default!;
        await Run(() => value = read());
        return value;
    }

    /// <summary>Waits until what is asserted holds, as a person would wait for the screen to show it.</summary>
    public async Task Until(Action assertion)
    {
        var deadline = DateTime.UtcNow + patience;

        while (true)
        {
            var attempt = Run(assertion);
            await attempt.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (attempt.IsCompletedSuccessfully || DateTime.UtcNow >= deadline)
            {
                await attempt;
                return;
            }

            await Task.Delay(pause);
        }
    }

    #endregion

    #region Private Methods

    // Work that fails before its first wait fails the task it is waited for by, never the loop.
    private static Task Start(Func<Task> work)
    {
        try
        {
            return work();
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private void Enqueue(Action work)
    {
        if (!queue.IsAddingCompleted)
        {
            queue.Add(work);
        }
    }

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var work in queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    #endregion
}
