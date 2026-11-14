namespace Drover.Server.Sandboxes;

/// <summary>
/// Clears up retired and interrupted sandboxes once when the server starts and then every minute, for as long as the
/// server is up. Registered only on a server with a sandbox provider. All of the work is in <c>Drover.Engine</c>. A
/// pass that fails is logged and the next one tries again.
/// </summary>
public sealed class SandboxSweeper(SandboxSweep sweep, TimeProvider time, ILogger<SandboxSweeper> logger) : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SandboxSweep.Interval, time);

        try
        {
            do
            {
                await SweepOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException stopping)
        {
            logger.LogDebug("The sandbox sweeper is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion

    #region Private Methods

    private async Task SweepOnce(CancellationToken stoppingToken)
    {
        try
        {
            var changed = await sweep.Sweep(stoppingToken);

            if (changed > 0)
            {
                logger.LogInformation("Cleared up {Count} retired or interrupted sandboxes", changed);
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Only the kind of failure: its text could hold what the provider sent.
            logger.LogWarning("Looking for sandboxes to clear up failed: {Failure}", failure.GetType().Name);
        }
    }

    #endregion
}
