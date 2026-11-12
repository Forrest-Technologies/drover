namespace Drover.Server.Health;

public static class DatabaseStartup
{
    #region Public Methods

    public static string ApplicationConnection(IConfiguration configuration, bool allowCustomRole = false)
    {
        var explicitConnection = configuration.GetConnectionString("Drover");
        var connection = explicitConnection is null
            ? Connection(configuration, "drover_app", "Database:AppPasswordFile")
            : new NpgsqlConnectionStringBuilder(explicitConnection);

        if ((!allowCustomRole && connection.Username is not "drover_app") || string.IsNullOrWhiteSpace(connection.Username)
            || string.IsNullOrWhiteSpace(connection.Host)
            || string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException("Drover requires a complete database connection permitted by the deployment policy.");
        }

        return connection.ConnectionString;
    }

    public static async Task Initialize(IConfiguration configuration)
    {
        var admin = Connection(configuration, "postgres", "Database:PasswordFile");
        var owner = Connection(configuration, "drover_owner", "Database:OwnerPasswordFile");
        var app = Connection(configuration, "drover_app", "Database:AppPasswordFile");
        await DatabaseBootstrap.Run(admin.ConnectionString, owner.Password!, app.Password!);
        await DatabaseMigrator.Run(owner.ConnectionString);
    }

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

        var path = configuration[passwordSetting];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"{passwordSetting} must name a readable password file.");
        }

        var password = File.ReadAllText(path).Trim();
        if (password.Length is 0)
        {
            throw new InvalidOperationException($"{passwordSetting} must not be empty.");
        }

        return new()
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = role,
            Password = password,
            Timeout = settings.TimeoutSeconds,
        };
    }

    #endregion
}
