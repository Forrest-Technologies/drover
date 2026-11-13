namespace Drover.Server.Policy;

/// <summary>
/// Where the deployment keeps the migration credential out of the serving process, checks at start that the
/// request connection is really the application role: not the owner, not a superuser, nothing that bypasses
/// row-level security. A configuration key says what was intended; the database says what the connection is.
/// Where the deployment allows another role, a connection that bypasses row-level security is still logged as an
/// error at start, and the status page shows it red.
/// </summary>
public sealed class DatabaseRoleGuard(
    IConfiguration configuration,
    IOptions<DeploymentPolicyOptions> options,
    IOptions<DatabaseOptions> database,
    ILogger<DatabaseRoleGuard> logger) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var strict = !options.Value.Rules.AllowMigrationCredentialInServer;
        var connectionString = string.IsNullOrEmpty(database.Value.ConnectionString)
            ? DatabaseStartup.ApplicationConnection(configuration, allowCustomRole: true)
            : database.Value.ConnectionString;
        DatabaseRole role;

        try
        {
            role = await DatabaseRole.Inspect(connectionString, cancellationToken);
        }
        catch (Exception failure) when (!strict && failure is NpgsqlException or TimeoutException or IOException)
        {
            // Where any role is allowed, a database that is not up yet does not stop the server: ready says so.
            logger.LogWarning("The database could not be asked which role the server is connected as: {Failure}", failure.GetType().Name);
            return;
        }

        if (strict && !role.IsApplicationRole)
        {
            throw new InvalidOperationException(
                $"{nameof(DeploymentPolicy.AllowMigrationCredentialInServer)} is off, so the server must connect as {DatabaseRole.Application} with no superuser or row-level-security bypass. The connection is '{role.Name}' (superuser: {role.IsSuperuser}, bypasses row-level security: {role.BypassesRowLevelSecurity}).");
        }

        if (role.IsSuperuser || role.BypassesRowLevelSecurity)
        {
            // Allowed by this deployment's policy, and still the one mistake that takes isolation away silently.
            logger.LogError(
                "The server is connected to the database as '{Role}', which bypasses row-level security: nothing in the database keeps one space's data from another. Connect as {ApplicationRole}",
                role.Name,
                DatabaseRole.Application);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
