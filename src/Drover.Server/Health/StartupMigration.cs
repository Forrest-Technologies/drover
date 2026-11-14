namespace Drover.Server.Health;

/// <summary>What the serving app does about the database's migrations before it serves a request.</summary>
public enum StartupMigrationMode
{
    /// <summary>Nothing: the one-off <c>--initialize-database</c> step is how migrations are applied.</summary>
    Off,

    /// <summary>
    /// Apply pending migrations as <c>drover_owner</c> only. The roles already exist from an earlier initialization,
    /// so the superuser password need not be kept on the serving app.
    /// </summary>
    OwnerOnly,

    /// <summary>The whole initialization, as the one-off step does it: roles, passwords, then migrations.</summary>
    Full,
}

/// <summary>
/// Lets an upgrade work without a separate step: where the deployment allows the migration credential in the
/// serving process and the process was given it, the pending migrations are applied before the first request.
/// Both steps are safe to repeat and to run from several instances at once (an advisory lock for the roles, EF Core's
/// migration lock for the schema). The passwords are read from configuration, used for the one connection, and
/// not kept; the server then serves as <c>drover_app</c> as ever.
/// </summary>
public static class StartupMigration
{
    #region Public Methods

    /// <summary>
    /// What to do at start. Only a connection built from <c>Database:*</c> settings qualifies: an explicit
    /// <c>ConnectionStrings:Drover</c> is a custom deployment, which initializes its database separately.
    /// </summary>
    public static StartupMigrationMode ModeFor(IConfiguration configuration, bool allowMigrationCredentialInServer)
    {
        if (!allowMigrationCredentialInServer || configuration.GetConnectionString("Drover") is not null
            || !IsSet(configuration, DatabaseStartup.OwnerPassword))
        {
            return StartupMigrationMode.Off;
        }

        return IsSet(configuration, DatabaseStartup.AdminPassword) ? StartupMigrationMode.Full : StartupMigrationMode.OwnerOnly;
    }

    /// <summary>
    /// Applies the migrations. Returns false after logging a critical error that names no secret, so the caller
    /// exits non-zero and the platform keeps the old build serving.
    /// </summary>
    public static async Task<bool> Apply(IConfiguration configuration, StartupMigrationMode mode)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole());
        var logger = loggerFactory.CreateLogger("Drover.Startup");

        try
        {
            if (mode is StartupMigrationMode.Full)
            {
                await DatabaseStartup.Initialize(configuration);
            }
            else
            {
                await DatabaseStartup.Migrate(configuration);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the kind of failure and PostgreSQL's code: a message can carry a host, a role or a statement.
            logger.LogCritical(
                "Applying the database migrations at start failed ({Failure}, SQL state {SqlState}). The server did not start. {Advice}",
                exception.GetType().Name,
                (exception as PostgresException)?.SqlState ?? "none",
                mode is StartupMigrationMode.OwnerOnly
                    ? "Check Database:OwnerPassword. On a database that was never initialized the roles do not exist yet: also give Database:Password (the superuser) once."
                    : "Check Database:Host and the superuser and owner passwords, or run the --initialize-database step instead.");
            return false;
        }

        logger.LogInformation("The database migrations are current: pending ones were applied before the server started ({Mode})", mode);
        return true;
    }

    #endregion

    #region Private Methods

    private static bool IsSet(IConfiguration configuration, string passwordSetting) =>
        !string.IsNullOrWhiteSpace(configuration[passwordSetting])
        || !string.IsNullOrWhiteSpace(configuration[$"{passwordSetting}{DatabaseStartup.FileSuffix}"]);

    #endregion
}
