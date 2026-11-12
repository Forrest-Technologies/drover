namespace Drover.Adapters.Tests;

/// <summary>
/// A real PostgreSQL for the adapter tests, set up the way the server sets up its own: roles bootstrapped with
/// passwords made for this run, migrations applied as <c>drover_owner</c>, connections as <c>drover_app</c>. It uses
/// the server named by <c>DROVER_TEST_POSTGRES</c> and starts a container when that is not set.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    #region Private Fields

    private readonly string databaseName = $"drover_adapters_{Guid.NewGuid():N}";

    private readonly string ownerPassword = NewPassword();

    private readonly string appPassword = NewPassword();

    private PostgreSqlContainer? container;

    private NpgsqlConnectionStringBuilder serverConnection = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    public string AppConnectionString => ConnectionStringFor("drover_app", appPassword);

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        serverConnection = new NpgsqlConnectionStringBuilder(await FindServer());
        await ExecuteOnServer($"CREATE DATABASE {databaseName}");
        var admin = new NpgsqlConnectionStringBuilder(serverConnection.ConnectionString) { Database = databaseName };
        await DatabaseBootstrap.Run(admin.ConnectionString, ownerPassword, appPassword);
        var owner = ConnectionStringFor("drover_owner", ownerPassword);
        await DatabaseMigrator.Run(owner);
        await CreateCheckpointTable(owner);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServer($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    #endregion

    #region Public Methods

    public ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverData(AppConnectionString);
        return services;
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

    /// <summary>
    /// The Phase 1 <c>workflow_checkpoints</c> table, made here for the checkpoint proof only. It is isolated by space
    /// and audience exactly as <c>IsolateBySpace</c> with its audience option isolates <c>agent_sessions</c>. The state is <c>json</c>, not <c>jsonb</c>: the framework's
    /// polymorphic JSON needs its "$type" property kept first, and jsonb reorders object keys.
    /// </summary>
    private static async Task CreateCheckpointTable(string ownerConnectionString)
    {
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(
            """
            CREATE TABLE workflow_checkpoints (
              space_id uuid NOT NULL DEFAULT sys.current_space(),
              audience_user_id uuid NOT NULL,
              session_id text NOT NULL,
              checkpoint_id text NOT NULL,
              parent_checkpoint_id text,
              state json NOT NULL,
              created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
              PRIMARY KEY (space_id, session_id, checkpoint_id));
            ALTER TABLE workflow_checkpoints ENABLE ROW LEVEL SECURITY;
            ALTER TABLE workflow_checkpoints FORCE ROW LEVEL SECURITY;
            CREATE POLICY space_isolation ON workflow_checkpoints FOR ALL
              USING (space_id = sys.current_space()) WITH CHECK (space_id = sys.current_space());
            CREATE POLICY audience ON workflow_checkpoints AS RESTRICTIVE FOR ALL
              USING (audience_user_id IN (sys.everyone(), sys.current_user_id()))
              WITH CHECK (audience_user_id IN (sys.everyone(), sys.current_user_id()));
            GRANT SELECT, INSERT, UPDATE, DELETE ON workflow_checkpoints TO drover_app;
            """,
            connection).ExecuteNonQueryAsync();
    }

    private async Task ExecuteOnServer(string sql)
    {
        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    private string ConnectionStringFor(string role, string password) =>
        new NpgsqlConnectionStringBuilder(serverConnection.ConnectionString)
        {
            Database = databaseName,
            Username = role,
            Password = password,
            Timeout = 10,
        }.ConnectionString;

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    #endregion
}


[CollectionDefinition(Name)]
public sealed class PostgresFixtureDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
