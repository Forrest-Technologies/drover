namespace Drover.Data.Tests.Isolation;

/// <summary>
/// A database of its own, migrated like the real one, where a test makes tables and functions that break a rule
/// on purpose. The catalog audit and the generated checks must catch the break; after the test corrects it they
/// must pass. The main database never sees any of it.
/// </summary>
public sealed class ScratchDatabase(
    string adminConnectionString,
    string ownerConnectionString,
    string appConnectionString,
    Func<Task> drop) : IAsyncDisposable
{
    #region Properties

    public string AppConnectionString => appConnectionString;

    #endregion

    #region Public Methods

    /// <summary>Runs SQL as <c>drover_owner</c>, the role that makes tables and functions.</summary>
    public Task Run(string sql) => Execute(ownerConnectionString, sql);

    /// <summary>Runs SQL as the superuser, for what the owner cannot do: handing a table to another role.</summary>
    public Task RunAsAdmin(string sql) => Execute(adminConnectionString, sql);

    /// <summary>Runs the real policy helpers, so a scratch table gets the policies a migration would give it.</summary>
    public async Task Migrate(Action<MigrationBuilder> build)
    {
        var migration = new MigrationBuilder(activeProvider: null);
        build(migration);

        foreach (var operation in migration.Operations.OfType<SqlOperation>())
        {
            await Run(operation.Sql);
        }
    }

    public ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDroverData(appConnectionString);
        return services.BuildServiceProvider();
    }

    public async Task<IReadOnlyList<string>> Audit(
        OwnershipMap ownership,
        Func<CatalogAudit, Task<IReadOnlyList<string>>> check,
        IReadOnlyList<string>? systemFunctions = null)
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        return await check(new CatalogAudit(connection, ownership, systemFunctions ?? ManifestFile.ReadSystemFunctions()));
    }

    public async Task<IsolationHarness> Harness(OwnershipMap ownership, params TableSeeder[] seeders) =>
        await IsolationHarness.Create(BuildServices(), appConnectionString, ownership, seeders);

    public async ValueTask DisposeAsync()
    {
        foreach (var connectionString in new[] { adminConnectionString, ownerConnectionString, appConnectionString })
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        }

        await drop();
    }

    #endregion

    #region Private Methods

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    #endregion
}
