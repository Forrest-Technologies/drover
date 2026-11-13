namespace Drover.Server.Health;

/// <summary>
/// The names, tags, and extra states of the health checks behind the status page. The checks are ASP.NET Core
/// health checks, used directly: <c>/health/ready</c> runs the ones tagged ready, the status report the ones
/// tagged status.
/// </summary>
public static class StatusChecks
{
    #region Public Methods

    public static IServiceCollection AddDroverStatus(this IServiceCollection services, string keyRingPath)
    {
        services.Configure<KeyRingLocation>(location => location.Path = keyRingPath);
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database_reachable", tags: [Ready])
            .AddCheck<DatabaseRoleHealthCheck>("database", tags: [Status])
            .AddCheck<MigrationsHealthCheck>("migrations", tags: [Status])
            .AddCheck<BackgroundWorkHealthCheck>("background_work", tags: [Status])
            .AddCheck<KeyRingHealthCheck>("key_ring", tags: [Status])
            .AddCheck<PublicAddressHealthCheck>("public_address", tags: [Status]);
        return services;
    }

    /// <summary>A result for something optional that is not set up: nothing is wrong, and nothing is there.</summary>
    public static HealthCheckResult Off(string description) =>
        HealthCheckResult.Healthy(description, new Dictionary<string, object> { [OffKey] = true });

    public static StatusRow ToRow(string name, HealthReportEntry entry) =>
        new(
            name,
            entry.Status switch
            {
                HealthStatus.Unhealthy => "failing",
                HealthStatus.Degraded => "attention",
                _ => entry.Data.ContainsKey(OffKey) ? "off" : "ok",
            },
            entry.Description ?? string.Empty);

    #endregion

    #region Constants

    public const string Ready = "ready";

    public const string Status = "status";

    private const string OffKey = "off";

    #endregion
}

/// <summary>Where the key ring is kept, so the status page can say whether it is there and can be written.</summary>
public sealed class KeyRingLocation
{
    public string Path { get; set; } = string.Empty;
}
