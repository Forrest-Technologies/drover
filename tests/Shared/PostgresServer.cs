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

    // A loaded runner can starve the harness's first connection. Retry it within this bound; on a healthy server
    // the first attempt succeeds and costs nothing extra.
    private static readonly TimeSpan connectionPatience = TimeSpan.FromSeconds(60);

    private readonly string ownerPassword = NewPassword();

    private readonly string appPassword = NewPassword();

    private readonly string templateName = $"drover_template_{Guid.NewGuid():N}";

    // Roles belong to the server, so two bootstraps at once race on them. Databases are made one at a time.
    private readonly SemaphoreSlim creating = new(1, 1);

    private PostgreSqlContainer? container;

    private NpgsqlConnectionStringBuilder server = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    /// <summary>
    /// The owner password this run gave <c>drover_owner</c>, for a test that runs the real one-off initialization
    /// against these roles. Giving it the same value changes nothing.
    /// </summary>
    public string OwnerPassword => ownerPassword;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        server = [with(await FindServer())];
        await ExecuteOnServer($"CREATE DATABASE {templateName}", retryConnection: true);
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

    private async Task ExecuteOnServer(string sql, bool retryConnection = false)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        var deadline = retryConnection ? Environment.TickCount64 + (long)connectionPatience.TotalMilliseconds : 0;
        var delay = TimeSpan.FromMilliseconds(500);

        while (true)
        {
            try
            {
                await connection.OpenAsync();
                break;
            }
            catch (Exception exception) when (retryConnection && (exception is NpgsqlException or TimeoutException))
            {
                if (Environment.TickCount64 >= deadline)
                {
                    throw;
                }

                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
            }
        }

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
