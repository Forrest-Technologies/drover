namespace Drover.Server.Memory;

/// <summary>
/// Retries refused conversation and person forget requests against the memory service once an hour for as long as the
/// server is running. A failed pass is logged and the next pass tries again.
/// </summary>
public sealed class MemoryForgetSweeper(
    MemoryForgetRetry retry,
    TimeProvider time,
    ILogger<MemoryForgetSweeper> logger) : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(MemoryForgetRetry.Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var forgotten = await retry.RetryDue(stoppingToken);

                    if (forgotten > 0)
                    {
                        logger.LogInformation("Completed {Count} pending memory service forget requests", forgotten);
                    }
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    logger.LogWarning("Retrying pending memory forget requests failed: {Failure}", failure.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException stopping) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("The memory forget sweeper is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion
}
