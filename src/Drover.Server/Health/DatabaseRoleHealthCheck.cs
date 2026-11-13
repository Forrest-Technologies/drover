namespace Drover.Server.Health;

/// <summary>
/// Asks the database who the server is connected as. Isolation between spaces is row-level security, and a
/// superuser or a role that bypasses it is not bound by it: pointed at such a connection, the server would work
/// and protect nothing. So this row is red then, whatever the deployment policy allows.
/// </summary>
public sealed class DatabaseRoleHealthCheck(IOptions<DatabaseOptions> database) : IHealthCheck
{
    #region Interface Implementations

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Judge(await DatabaseRole.Inspect(database.Value.ConnectionString, cancellationToken));
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or IOException)
        {
            return HealthCheckResult.Unhealthy("The database cannot be reached.", failure);
        }
    }

    #endregion

    #region Public Methods

    public static HealthCheckResult Judge(DatabaseRole role) =>
        role switch
        {
            { IsApplicationRole: true } => HealthCheckResult.Healthy(
                $"Connected as {DatabaseRole.Application}, which cannot bypass row-level security."),
            { IsSuperuser: true } or { BypassesRowLevelSecurity: true } => HealthCheckResult.Unhealthy(
                $"Connected as '{role.Name}', which bypasses row-level security: nothing in the database keeps one space's data from another. Connect as {DatabaseRole.Application}."),
            _ => HealthCheckResult.Degraded(
                $"Connected as '{role.Name}'. It cannot bypass row-level security, but it is not {DatabaseRole.Application} and may be able to change the schema."),
        };

    #endregion
}
