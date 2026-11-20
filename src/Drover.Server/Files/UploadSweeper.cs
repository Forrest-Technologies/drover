namespace Drover.Server.Files;

/// <summary>
/// Removes abandoned conversation uploads once at startup and then every minute. A failed pass is logged and the
/// next pass retries durable pending storage deletions.
/// </summary>
public sealed class UploadSweeper(UploadCleanup cleanup, TimeProvider time, ILogger<UploadSweeper> logger)
    : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(UploadCleanup.Interval, time);

        try
        {
            do
            {
                await SweepOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException stopping) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("The upload sweeper is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion

    #region Private Methods

    private async Task SweepOnce(CancellationToken stoppingToken)
    {
        try
        {
            var removed = await cleanup.Sweep(stoppingToken);

            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} abandoned conversation uploads", removed);
            }
        }
        catch (Exception failure) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(failure, "Looking for abandoned conversation uploads failed");
        }
    }

    #endregion
}
