namespace Drover.Data.Tests;

/// <summary>
/// One real PostgreSQL for the whole data test run. It uses the server named by <c>DROVER_TEST_POSTGRES</c> (a
/// superuser connection string, see scripts/dev-postgres.sh) and starts a container when that is not set. It makes
/// a database of its own, bootstraps the roles with passwords generated for this run, applies the migrations as
/// <c>drover_owner</c>, and hands out connections as <c>drover_app</c>. Roles belong to the server, not the
/// database, so two test runs against one server at the same time would reset each other's passwords.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    #region Private Fields

    private readonly string databaseName = $"drover_test_{Guid.NewGuid():N}";

    private readonly string ownerPassword = NewPassword();

    private readonly string appPassword = NewPassword();

    private PostgreSqlContainer? container;

    private NpgsqlConnectionStringBuilder serverConnection = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    public string AdminConnectionString => ForDatabase(serverConnection).ConnectionString;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        serverConnection = new NpgsqlConnectionStringBuilder(await FindServer());
        await CreateDatabase();
        await DatabaseBootstrap.Run(AdminConnectionString, ownerPassword, appPassword);
        await DatabaseMigrator.Run(ConnectionStringFor("drover_owner", ownerPassword).ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection).ExecuteNonQueryAsync();

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>A connection string for <c>drover_app</c>. <paramref name="applicationName"/> tells it apart in pg_stat_activity.</summary>
    public string AppConnectionString(int maxPoolSize = 10, bool noResetOnClose = false, string? applicationName = null)
    {
        var builder = ConnectionStringFor("drover_app", appPassword);
        builder.MaxPoolSize = maxPoolSize;
        builder.NoResetOnClose = noResetOnClose;
        builder.Timeout = 10;

        if (applicationName is not null)
        {
            builder.ApplicationName = applicationName;
        }

        return builder.ConnectionString;
    }

    public Task Bootstrap() => DatabaseBootstrap.Run(AdminConnectionString, ownerPassword, appPassword);

    public string RoleConnectionString(string role, string password) =>
        ConnectionStringFor(role, password).ConnectionString;

    public ServiceProvider BuildServices(int maxPoolSize = 10, bool noResetOnClose = false, string? applicationName = null)
    {
        var services = new ServiceCollection();
        services.AddDroverData(AppConnectionString(maxPoolSize, noResetOnClose, applicationName));
        return services.BuildServiceProvider();
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

    private async Task CreateDatabase()
    {
        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection).ExecuteNonQueryAsync();
    }

    private NpgsqlConnectionStringBuilder ConnectionStringFor(string role, string password)
    {
        var builder = ForDatabase(serverConnection);
        builder.Username = role;
        builder.Password = password;
        return builder;
    }

    private NpgsqlConnectionStringBuilder ForDatabase(NpgsqlConnectionStringBuilder server) =>
        new(server.ConnectionString) { Database = databaseName };

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    #endregion
}
