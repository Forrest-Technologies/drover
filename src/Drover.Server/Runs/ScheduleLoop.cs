namespace Drover.Server.Runs;

/// <summary>
/// The scheduler: sends the schedules that have come due, once when the server starts and then every
/// <see cref="ScheduleRunner.Interval"/>, for as long as the server is up. Every server runs one; the claim in
/// <c>sys.claim_due_schedules</c> sends each schedule once however many there are. All of the work is in
/// <c>Drover.Engine</c>. A pass that fails is logged and the next one tries again.
/// </summary>
public sealed class ScheduleLoop(ScheduleRunner runner, TimeProvider time, ILogger<ScheduleLoop> logger) : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScheduleRunner.Interval, time);

        try
        {
            do
            {
                await SendOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException stopping)
        {
            logger.LogDebug("The scheduler is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion

    #region Private Methods

    private async Task SendOnce(CancellationToken stoppingToken)
    {
        try
        {
            var handled = await runner.SendDue(stoppingToken);

            if (handled > 0)
            {
                logger.LogInformation("Handled {Count} due schedules", handled);
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning("Looking for due schedules failed: {Failure}", failure.GetType().Name);
        }
    }

    #endregion
}
