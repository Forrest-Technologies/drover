namespace Drover.Server.Models;

/// <summary>
/// Renews people's model sign-ins that have sat unused, once an hour, for as long as the server is up. All of the
/// work is in <c>Drover.Engine</c>. A pass that fails is logged and the next one tries again.
/// </summary>
public sealed class ModelSignInRenewer(ModelSignInRenewal renewal, TimeProvider time, ILogger<ModelSignInRenewer> logger)
    : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ModelSignInRenewal.Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var renewed = await renewal.RenewDue(stoppingToken);

                    if (renewed > 0)
                    {
                        logger.LogInformation("Renewed {Count} model sign-ins that sat unused", renewed);
                    }
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // Only the kind of failure: its text could hold what a provider sent.
                    logger.LogWarning("Looking for model sign-ins to renew failed: {Failure}", failure.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException stopping)
        {
            logger.LogDebug("The model sign-in renewer is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion
}
