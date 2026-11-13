namespace Drover.Server.Policy;

/// <summary>
/// Checks at start who the request connection really is. Where the deployment keeps the migration credential out
/// of the serving process, anything but the application role stops the server. Where it does not, the server
/// starts, but a role that can step around row-level security is logged as an error: isolation between spaces has
/// silently stopped working. A configuration key says what was intended; the database says what the connection is.
/// </summary>
public sealed class DatabaseRoleGuard(IConfiguration configuration, IOptions<DeploymentPolicyOptions> options,
    IOptions<DatabaseOptions> database, ILogger<DatabaseRoleGuard> logger) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var strict = !options.Value.Rules.AllowMigrationCredentialInServer;

        if (!strict && string.IsNullOrEmpty(database.Value.ConnectionString))
        {
            return;
        }

        var connectionString = string.IsNullOrEmpty(database.Value.ConnectionString)
            ? DatabaseStartup.ApplicationConnection(configuration, allowCustomRole: true)
            : database.Value.ConnectionString;
        DatabaseRole role;

        try
        {
            role = await DatabaseRole.Inspect(connectionString, cancellationToken);
        }
        catch (Exception exception) when (!strict && exception is NpgsqlException or TimeoutException or IOException)
        {
            // Not fatal here: readiness reports the outage, and the role is checked again on the status page.
            logger.LogWarning("The database role could not be checked at start because PostgreSQL did not answer");
            return;
        }

        if (strict && !role.IsApplicationRole)
        {
            throw new InvalidOperationException(
                $"{nameof(DeploymentPolicy.AllowMigrationCredentialInServer)} is off, so the server must connect as {DatabaseRole.Application} with no superuser or row-level-security bypass. The connection is '{role.Name}' (superuser: {role.IsSuperuser}, bypasses row-level security: {role.BypassesRowLevelSecurity}).");
        }

        if (role.IsSuperuser || role.BypassesRowLevelSecurity)
        {
            logger.LogError(
                "The server connects to PostgreSQL as {Role}, which can step around row-level security, so one space's data is not kept from another. Connect as {Application}",
                role.Name,
                DatabaseRole.Application);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
