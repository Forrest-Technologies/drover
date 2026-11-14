namespace Drover.Server.Health;

/// <summary>The three connections the one-off initialization uses: superuser, migration owner, application.</summary>
public sealed record InitializationConnections(
    NpgsqlConnectionStringBuilder Admin,
    NpgsqlConnectionStringBuilder Owner,
    NpgsqlConnectionStringBuilder App);

/// <summary>
/// Builds database connections from settings. Each password is given one way: as a value (for example the
/// environment variable <c>Database__AppPassword</c>) or as a file (<c>Database__AppPasswordFile</c>). Both are
/// checked alike, and no message ever carries a password.
/// </summary>
public static class DatabaseStartup
{
    #region Private Fields

    // Setting names only, written out: the start-up log names them, and they must not be derived from anything that
    // holds a password value.
    private static readonly string[] initializationOnlySettings =
    [
        "ConnectionStrings:DroverOwner",
        "Database:Password",
        "Database:PasswordFile",
        "Database:OwnerPassword",
        "Database:OwnerPasswordFile",
    ];

    #endregion

    #region Public Methods

    public static string ApplicationConnection(IConfiguration configuration, bool allowCustomRole = false)
    {
        var explicitConnection = configuration.GetConnectionString("Drover");
        var connection = explicitConnection is null
            ? Connection(configuration, "drover_app", AppPassword)
            : [with(explicitConnection)];

        if ((!allowCustomRole && connection.Username is not "drover_app") || string.IsNullOrWhiteSpace(connection.Username)
            || string.IsNullOrWhiteSpace(connection.Host)
            || string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Drover requires a complete database connection permitted by the deployment policy.");
        }

        return connection.ConnectionString;
    }

    public static InitializationConnections InitializationConnections(IConfiguration configuration) =>
        new(
            Connection(configuration, "postgres", AdminPassword),
            Connection(configuration, "drover_owner", OwnerPassword),
            Connection(configuration, "drover_app", AppPassword));

    /// <summary>
    /// The settings only the one-off initialization needs that this configuration holds: the owner connection, or
    /// the superuser or owner password in either form. Only whether each is set is read, never its value.
    /// </summary>
    public static IReadOnlyList<string> HeldInitializationSettings(IConfiguration configuration) =>
    [
        .. initializationOnlySettings.Where(setting => !string.IsNullOrWhiteSpace(configuration[setting])),
    ];

    public static async Task Initialize(IConfiguration configuration)
    {
        var (admin, owner, app) = InitializationConnections(configuration);
        await DatabaseBootstrap.Run(admin.ConnectionString, owner.Password!, app.Password!);
        await DatabaseMigrator.Run(owner.ConnectionString);
    }

    /// <summary>
    /// Applies pending migrations as the owner and nothing else, for a database whose roles already exist. It reads
    /// only the owner password, so the superuser password need not be given.
    /// </summary>
    public static async Task Migrate(IConfiguration configuration) =>
        await DatabaseMigrator.Run(Connection(configuration, "drover_owner", OwnerPassword).ConnectionString);

    #endregion

    #region Constants

    /// <summary>The superuser password. Only the one-off initialization reads it.</summary>
    public const string AdminPassword = "Database:Password";

    /// <summary>The migration owner's password. Only the one-off initialization reads it.</summary>
    public const string OwnerPassword = "Database:OwnerPassword";

    public const string AppPassword = "Database:AppPassword";

    /// <summary>The setting that names a file holding the password instead.</summary>
    public const string FileSuffix = "File";

    #endregion

    #region Private Methods

    private static NpgsqlConnectionStringBuilder Connection(
        IConfiguration configuration,
        string role,
        string passwordSetting)
    {
        var settings = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new InvalidOperationException("Database:Host and Database:Database must name the database.");
        }

        return new()
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = role,
            Password = ReadPassword(configuration, passwordSetting),
            Timeout = settings.TimeoutSeconds,
            SslMode = settings.SslMode,
        };
    }

    private static string ReadPassword(IConfiguration configuration, string setting)
    {
        var fileSetting = $"{setting}{FileSuffix}";
        var value = configuration[setting];
        var path = configuration[fileSetting];

        var password = (string.IsNullOrEmpty(value), string.IsNullOrEmpty(path)) switch
        {
            (false, false) => throw new InvalidOperationException(
                $"Both {setting} and {fileSetting} are set. Give the password one way only."),
            (true, true) => throw new InvalidOperationException(
                $"Give the password as {setting} or name a file holding it in {fileSetting}."),
            (false, true) => value!.Trim(),
            (true, false) => ReadPasswordFile(path!, fileSetting),
        };

        if (password.Length is 0)
        {
            throw new InvalidOperationException(
                $"{(string.IsNullOrEmpty(value) ? fileSetting : setting)} must not be empty.");
        }

        return password;
    }

    private static string ReadPasswordFile(string path, string fileSetting)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"{fileSetting} must name a readable password file.");
        }

        return File.ReadAllText(path).Trim();
    }

    #endregion
}
