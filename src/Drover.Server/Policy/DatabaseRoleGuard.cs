namespace Drover.Server.Policy;

/// <summary>
/// Where the deployment keeps the migration credential out of the serving process, checks at start that the
/// request connection is really the application role: not the owner, not a superuser, nothing that bypasses
/// row-level security. A configuration key says what was intended; the database says what the connection is.
/// </summary>
public sealed class DatabaseRoleGuard(IConfiguration configuration, IOptions<DeploymentPolicyOptions> options) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Rules.AllowMigrationCredentialInServer)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("Drover")
            ?? throw new InvalidOperationException("ConnectionStrings:Drover is not set.");
        var role = await DatabaseRole.Inspect(connectionString, cancellationToken);

        if (!role.IsApplicationRole)
        {
            throw new InvalidOperationException(
                $"{nameof(DeploymentPolicy.AllowMigrationCredentialInServer)} is off, so the server must connect as {DatabaseRole.Application} with no superuser or row-level-security bypass. The connection is '{role.Name}' (superuser: {role.IsSuperuser}, bypasses row-level security: {role.BypassesRowLevelSecurity}).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
