namespace Drover.Server.Health;

/// <summary>
/// Ready also means the database has every migration this build carries. When the one-off
/// <c>--initialize-database</c> step was skipped on a deploy, traffic must stay off the new build until it has run.
/// A database that is ahead of the build (after a rollback) is not ready either, as on the status page: the
/// older build does not know the schema it would write to, and failing loudly beats failing on one request.
/// </summary>
public sealed class MigrationHealthCheck(StatusService status) : IHealthCheck
{
    #region Interface Implementations

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await status.ReadMigrations(cancellationToken);
            return MigrationAdvice.Readiness(state) is { } advice
                ? HealthCheckResult.Unhealthy(advice)
                : HealthCheckResult.Healthy("The database has every migration this build carries.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or IOException)
        {
            // The database check reports the outage and its own reason; this one only says it could not tell.
            return HealthCheckResult.Unhealthy("The database's migrations could not be read.", exception);
        }
    }

    #endregion
}
