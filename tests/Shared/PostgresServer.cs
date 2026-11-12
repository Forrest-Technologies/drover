namespace Drover.Tests.Common;

/// <summary>
/// One real PostgreSQL for a test assembly. It uses the server named by <c>DROVER_TEST_POSTGRES</c> (a superuser
/// connection string, see scripts/dev-postgres.sh) and starts a container when that is not set. It migrates one
/// template database, and each test class copies that template into a database of its own. Role passwords are
/// generated for the run. Roles belong to the server, not the database, so two test assemblies run against one
/// named server at the same time would reset each other's passwords: run them one after the other there.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    #region Private Fields

    private readonly string ownerPassword = NewPassword();

    private readonly string appPassword = NewPassword();

    private readonly string templateName = $"drover_template_{Guid.NewGuid():N}";

    // Roles belong to the server, so two bootstraps at once race on them. Databases are made one at a time.
    private readonly SemaphoreSlim creating = new(1, 1);

    private PostgreSqlContainer? container;

    private NpgsqlConnectionStringBuilder server = null!; // set first thing in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        server = new NpgsqlConnectionStringBuilder(await FindServer());
        await ExecuteOnServer($"CREATE DATABASE {templateName}");
        await DatabaseBootstrap.Run(ForDatabase(templateName).ConnectionString, ownerPassword, appPassword);
        await DatabaseMigrator.Run(ForRole("drover_owner", ownerPassword, templateName).ConnectionString);
        NpgsqlConnection.ClearAllPools();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServer($"DROP DATABASE IF EXISTS {templateName} WITH (FORCE)");

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>A migrated database of its own, with the roles granted. Disposing it drops it.</summary>
    public async Task<TestDatabase> CreateDatabase()
    {
        var name = $"drover_test_{Guid.NewGuid():N}";
        await creating.WaitAsync();

        try
        {
            await ExecuteOnServer($"CREATE DATABASE {name} TEMPLATE {templateName}");

            // A copied database keeps its tables and grants but not its database-level privileges, so grant them again.
            await DatabaseBootstrap.Run(ForDatabase(name).ConnectionString, ownerPassword, appPassword);
        }
        finally
        {
            creating.Release();
        }

        var app = ForRole("drover_app", appPassword, name);
        app.Timeout = 10;
        return new TestDatabase(ForDatabase(name).ConnectionString, app.ConnectionString, () => Drop(name));
    }

    #endregion

    #region Private Methods

    private async Task<string> FindServer()
    {
        var named = Environment.GetEnvironmentVariable("DROVER_TEST_POSTGRES");

        if (!string.IsNullOrWhiteSpace(named))
        {
            return named;
        }

        container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    }

    // Not ClearAllPools: other tests in this assembly are using their own pools at the same time.
    private Task Drop(string name) => ExecuteOnServer($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");

    private async Task ExecuteOnServer(string sql)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    private NpgsqlConnectionStringBuilder ForDatabase(string database) =>
        new(server.ConnectionString) { Database = database };

    private NpgsqlConnectionStringBuilder ForRole(string role, string password, string database)
    {
        var builder = ForDatabase(database);
        builder.Username = role;
        builder.Password = password;
        return builder;
    }

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    #endregion
}

/// <summary>A database made for one test class. Disposing it drops it.</summary>
public sealed class TestDatabase(string adminConnectionString, string appConnectionString, Func<Task> drop)
    : IAsyncDisposable
{
    #region Properties

    public string AdminConnectionString { get; } = adminConnectionString;

    /// <summary>The connection string of <c>drover_app</c>, the role every request and job uses.</summary>
    public string AppConnectionString { get; } = appConnectionString;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync() => await drop();

    #endregion
}
