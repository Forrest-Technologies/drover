namespace Drover.Engine.Runs;

/// <summary>
/// The in-process half of waking workers and stopping runs. A request that queues a run wakes the dispatcher at once;
/// a request that cancels one stops it at once when this server is the one executing it. Other servers learn the
/// same things from the database: a notification for new work, and the cancel flag on the run row.
/// </summary>
public sealed class RunSignal : IDisposable
{
    #region Private Fields

    private readonly SemaphoreSlim wake = new(0, 1);

    private readonly Lock waking = new();

    private readonly ConcurrentDictionary<Guid, Action> executing = new();

    #endregion

    #region Public Methods

    /// <summary>There may be a run to claim. Signals collapse: one pending wake is enough.</summary>
    public void Wake()
    {
        lock (waking)
        {
            if (wake.CurrentCount == 0)
            {
                wake.Release();
            }
        }
    }

    /// <summary>Returns when woken, or after <paramref name="timeout"/> at the latest.</summary>
    public Task WaitForWork(TimeSpan timeout, CancellationToken cancellationToken) =>
        wake.WaitAsync(timeout, cancellationToken);

    /// <summary>Registers the way to stop a run this server is executing, until the registration is disposed.</summary>
    public IDisposable Executing(Guid runId, Action stop)
    {
        executing[runId] = stop;
        return new Registration(this, runId, stop);
    }

    /// <summary>Stops the run now if this server is executing it. The cancel flag on the row covers every other case.</summary>
    public void RequestCancel(Guid runId)
    {
        if (executing.TryGetValue(runId, out var stop))
        {
            stop();
        }
    }

    #endregion

    #region Interface Implementations

    public void Dispose() => wake.Dispose();

    #endregion

    #region Nested Types

    private sealed class Registration(RunSignal signal, Guid runId, Action stop) : IDisposable
    {
        // Only this registration's own entry: a newer claim of the same run may have registered since.
        public void Dispose() => signal.executing.TryRemove(new KeyValuePair<Guid, Action>(runId, stop));
    }

    #endregion
}
