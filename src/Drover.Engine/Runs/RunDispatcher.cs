namespace Drover.Engine.Runs;

/// <summary>
/// Claims runs and hands each to the executor, at most <see cref="RunOptions.Workers"/> at a time. One loop claims
/// for all of them: every call to <c>sys.claim_runs</c> writes an audit row, so an idle server asks once per poll
/// interval, not once per worker. It wakes at once when this server queues a run, when another server announces
/// one, and when a run finishes; the poll is only the fallback for a lost signal and for leases that expire.
/// </summary>
public sealed class RunDispatcher(
    RunScopes scopes,
    RunExecutor executor,
    RunSignal signal,
    RunWakeListener listener,
    IOptions<RunOptions> options,
    ILogger<RunDispatcher> logger)
{
    #region Private Fields

    private static readonly TimeSpan errorDelay = TimeSpan.FromSeconds(2);

    #endregion

    #region Properties

    /// <summary>The name this server's leases carry. A new one for every start, so a restarted server never mistakes an old lease for its own.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    #endregion

    #region Public Methods

    public async Task Run(CancellationToken stopping)
    {
        var listening = Listen(stopping);
        ConcurrentDictionary<Guid, Task> running = new();
        logger.LogInformation("Run dispatcher {WorkerId} started with {Workers} workers", WorkerId, options.Value.Workers);

        while (!stopping.IsCancellationRequested)
        {
            var claimedAll = await ClaimAndStart(running, stopping);

            if (!claimedAll)
            {
                try
                {
                    await signal.WaitForWork(options.Value.PollInterval, stopping);
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebug("Run dispatcher {WorkerId} is stopping", WorkerId);
                    break;
                }
            }
        }

        await Task.WhenAll(running.Values);
        await listening;
    }

    #endregion

    #region Private Methods

    /// <summary>True when every free worker got a run, so there may be more to claim right away.</summary>
    private async Task<bool> ClaimAndStart(ConcurrentDictionary<Guid, Task> running, CancellationToken stopping)
    {
        var free = options.Value.Workers - running.Count;

        if (free <= 0)
        {
            return false;
        }

        try
        {
            var claimed = await scopes.WithoutTenant(
                services => services.GetRequiredService<RunRepository>().Claim(WorkerId, free, stopping),
                stopping);

            foreach (var run in claimed)
            {
                running[run.RunId] = Execute(run, running, stopping);
            }

            return claimed.Count == free;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Run dispatcher {WorkerId} stopped while claiming", WorkerId);
            return false;
        }
        catch (ObjectDisposedException exception)
        {
            // The host's services are gone, so this dispatcher can never reach the database again.
            logger.LogDebug(exception, "Run dispatcher {WorkerId} lost its host", WorkerId);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Claiming runs failed; trying again shortly");
            await Task.Delay(errorDelay, CancellationToken.None);
            return false;
        }
    }

    private async Task Execute(ClaimedRun run, ConcurrentDictionary<Guid, Task> running, CancellationToken stopping)
    {
        // Let the dispatcher go back to claiming before this run does any work.
        await Task.Yield();

        try
        {
            await executor.Execute(run, WorkerId, stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            logger.LogInformation("Run {RunId} stopped with the server", run.RunId);
        }
        catch (Exception exception)
        {
            // The run keeps its lease until it expires; the next claim takes it over as another attempt.
            logger.LogError(exception, "Run {RunId} stopped on an error the executor did not handle", run.RunId);
        }
        finally
        {
            running.TryRemove(run.RunId, out _);
            signal.Wake();
        }
    }

    private async Task Listen(CancellationToken stopping)
    {
        try
        {
            await listener.Listen(signal.Wake, stopping);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Run dispatcher {WorkerId} stopped listening", WorkerId);
        }
    }

    #endregion
}
