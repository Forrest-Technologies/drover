namespace Drover.Server.Health;

/// <summary>
/// Ready means the application role can reach PostgreSQL. No tenant data is touched.
/// </summary>
public sealed class DatabaseHealthCheck(IOptions<DatabaseOptions> options) : IHealthCheck
{
    #region Interface Implementations

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return await TryConnect(new NpgsqlConnectionStringBuilder(settings.ConnectionString), cancellationToken);
        }

        if (settings.Host.Length is 0)
        {
            return HealthCheckResult.Unhealthy("Database:Host is not set.");
        }

        var password = await ReadPassword(settings, cancellationToken);
        if (password is null)
        {
            return HealthCheckResult.Unhealthy("Database:PasswordFile is not set or cannot be read.");
        }

        return await TryConnect(new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.Username,
            Password = password,
            Timeout = settings.TimeoutSeconds,
        }, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static async Task<string?> ReadPassword(DatabaseOptions settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.PasswordFile))
        {
            return null;
        }

        var password = await File.ReadAllTextAsync(settings.PasswordFile, cancellationToken);
        return password.Trim();
    }

    private static async Task<HealthCheckResult> TryConnect(
        NpgsqlConnectionStringBuilder builder,
        CancellationToken cancellationToken)
    {
        // A fresh connection must not hide a database outage behind a pooled connection.
        builder.Pooling = false;

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or IOException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }

    #endregion
}
