namespace Drover.Server.Tools;

/// <summary>
/// Looks for tool server sign-ins that are running out, every few minutes, for as long as the server is up. All of
/// the work is in <c>Drover.Engine</c>. A look that fails is logged and the next one tries again.
/// </summary>
public sealed class ToolSignInRenewer(ToolSignInRenewal renewal, IOptions<ToolSignInOptions> options, TimeProvider time, ILogger<ToolSignInRenewer> logger)
    : BackgroundService
{
    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RenewEvery, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var renewed = await renewal.RenewDue(stoppingToken);

                    if (renewed > 0)
                    {
                        logger.LogInformation("Renewed {Count} tool server sign-ins ahead of time", renewed);
                    }
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // Only the kind of failure: its text could hold what a tool server sent.
                    logger.LogWarning("Looking for tool server sign-ins to renew failed: {Failure}", failure.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException stopping)
        {
            logger.LogDebug("The tool server sign-in renewer is stopping: {Reason}", stopping.GetType().Name);
        }
    }

    #endregion
}
