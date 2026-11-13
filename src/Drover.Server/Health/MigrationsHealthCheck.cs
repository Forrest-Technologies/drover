namespace Drover.Server.Health;

/// <summary>Whether the database is at the version this build expects.</summary>
public sealed class MigrationsHealthCheck(ServerHealthService health) : IHealthCheck
{
    #region Interface Implementations

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await health.ReadSchemaVersion(cancellationToken);
            return version.IsCurrent
                ? HealthCheckResult.Healthy($"The database is at this build's version ({version.Applied}).")
                : HealthCheckResult.Unhealthy(
                    $"The database is at {version.Applied ?? "no version"} and this build expects {version.Expected}. Run the database initialization: with Docker, docker compose up --build does it.");
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or IOException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The database's version could not be read.", failure);
        }
    }

    #endregion
}
