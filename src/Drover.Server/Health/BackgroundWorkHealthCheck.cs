namespace Drover.Server.Health;

/// <summary>How much work waits for a worker, across all spaces, as counts. A lease that ran out means a worker stopped in the middle of a run.</summary>
public sealed class BackgroundWorkHealthCheck(ServerHealthService health) : IHealthCheck
{
    #region Interface Implementations

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var work = await health.ReadWork(cancellationToken);
            var counts = $"{work.Queued} queued, {work.Running} running, {work.Waiting} waiting for a person.";
            return work.ExpiredLeases > 0
                ? HealthCheckResult.Degraded($"{counts} {work.ExpiredLeases} running with a lease that ran out: a worker stopped in the middle, and another picks the run up.")
                : HealthCheckResult.Healthy(counts);
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or IOException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The work queue could not be read.", failure);
        }
    }

    #endregion
}
