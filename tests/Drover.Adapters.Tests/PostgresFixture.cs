namespace Drover.Adapters.Tests;

/// <summary>
/// A real PostgreSQL for the adapter tests, set up the way the server sets up its own: a migrated database of the
/// assembly's <see cref="PostgresServer"/>, connections as <c>drover_app</c>. It takes its database from that server
/// rather than bootstrapping the roles itself, because roles belong to the whole PostgreSQL server: a second set of
/// passwords for them would undo the first, and the tests of whichever fixture set them first could no longer sign in.
/// </summary>
public sealed class PostgresFixture(PostgresServer server) : IAsyncLifetime
{
    #region Private Fields

    private TestDatabase database = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    public string AppConnectionString => database.AppConnectionString;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        database = await server.CreateDatabase();
        var owner = new NpgsqlConnectionStringBuilder(database.AdminConnectionString)
        {
            Username = "drover_owner",
            Password = server.OwnerPassword,
        };
        await CreateCheckpointTable(owner.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await database.DisposeAsync();
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

    #endregion
}


[CollectionDefinition(Name)]
public sealed class PostgresFixtureDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
