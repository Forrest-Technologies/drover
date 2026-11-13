namespace Drover.Server.Health;

/// <summary>
/// Whether the key ring is where the server was told to keep it and can be written. Every stored secret is
/// encrypted with it: without it they cannot be read, and if it cannot be written the keys cannot be renewed.
/// </summary>
public sealed class KeyRingHealthCheck(IOptions<KeyRingLocation> location) : IHealthCheck
{
    #region Interface Implementations

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var path = location.Value.Path;

        if (!Directory.Exists(path))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The key ring's directory is not there. Stored secrets cannot be read without it."));
        }

        if (!Directory.EnumerateFiles(path, "*.xml").Any())
        {
            return Task.FromResult(HealthCheckResult.Degraded("The key ring holds no key yet. One is made when the first secret is stored."));
        }

        return Task.FromResult(WriteFailure(path) is { } failure
            ? HealthCheckResult.Unhealthy("The key ring cannot be written, so its keys cannot be renewed.", failure)
            : HealthCheckResult.Healthy("Present and writable. Back it up with the database: stored secrets cannot be read without it."));
    }

    #endregion

    #region Private Methods

    private static Exception? WriteFailure(string path)
    {
        var probe = Path.Combine(path, $".writable-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return failure;
        }
    }

    #endregion
}
