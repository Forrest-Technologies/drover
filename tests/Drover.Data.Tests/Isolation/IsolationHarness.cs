namespace Drover.Data.Tests.Isolation;

/// <summary>A table the generated checks run against: its shape from the catalog, its class, and its seeder.</summary>
public sealed record TableUnderTest(string Name, TableShape Shape, TableOwnership Ownership, TableSeeder Seeder)
{
    public string Quoted => $"\"{Name}\"";

    public OwnershipClass Class => Ownership.Class;
}

/// <summary>
/// Everything the generated checks need: tenants to act as, rows to act on, and plain SQL to run. It works from
/// the catalog and from seeders, never from entity types, so the same checks run on the real tables and on
/// scratch tables made inside a test.
/// </summary>
public sealed class IsolationHarness : IAsyncDisposable
{
    #region Private Fields

    private readonly ServiceProvider provider;

    private readonly OwnershipMap ownership;

    private readonly IReadOnlyList<TableShape> shapes;

    private readonly IReadOnlyList<TableSeeder> seeders;

    #endregion

    #region Constructors

    private IsolationHarness(
        ServiceProvider provider,
        OwnershipMap ownership,
        IReadOnlyList<TableShape> shapes,
        IReadOnlyList<TableSeeder> seeders)
    {
        this.provider = provider;
        this.ownership = ownership;
        this.shapes = shapes;
        this.seeders = seeders;
    }

    #endregion

    #region Public Methods

    public static async Task<IsolationHarness> Create(
        ServiceProvider provider,
        string appConnectionString,
        OwnershipMap ownership,
        IEnumerable<TableSeeder> seeders)
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        return new IsolationHarness(provider, ownership, await CatalogReader.Read(connection), [.. seeders]);
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    public Task<TenantWorld> NewWorld() => TenantWorld.Seed(provider);

    public Task<TenantSession> Open(Guid? spaceId, Guid? userId) => TenantSession.Open(provider, spaceId, userId);

    public TableUnderTest Table(string name)
    {
        var shape = shapes.FirstOrDefault(found => found.Name == name)
            ?? throw new InvalidOperationException($"Table '{name}' is not in the database.");
        var owned = ownership.Find(name)
            ?? throw new InvalidOperationException($"Table '{name}' has no ownership class.");
        var seeder = seeders.FirstOrDefault(found => found.Entity == owned.Entity)
            ?? throw new InvalidOperationException($"Table '{name}' has no seeder.");
        return new TableUnderTest(name, shape, owned, seeder);
    }

    /// <summary>The parent tables this table points at that belong to a space or a user, and so need a row of their own.</summary>
    public IReadOnlyList<TableUnderTest> TenantParents(TableUnderTest table, params OwnershipClass[] classes) =>
    [
        .. table.Shape.ForeignKeys
            .Select(key => key.ParentTable)
            .Where(parent => parent != table.Name)
            .Distinct()
            .Where(parent => ownership.Find(parent) is { } owned && classes.Contains(owned.Class))
            .Select(Table),
    ];

    #endregion

    #region Rows

    /// <summary>Inserts a row, and first a row in each parent table, all placed the same way. Commits as the placement's user.</summary>
    public async Task<SeededRow> Seed(TableUnderTest table, RowPlacement placement)
    {
        var parents = new Dictionary<string, SeededRow>();

        foreach (var parent in TenantParents(table, OwnershipClass.Space, OwnershipClass.SpaceWithAudience, OwnershipClass.User))
        {
            parents[parent.Name] = await Seed(parent, placement);
        }

        var values = await Compose(table, placement, parents);
        await using var session = await Open(placement.SpaceId, placement.User);
        var key = await Insert(session, table, values);
        await session.Commit();
        return new SeededRow(table.Name, key, placement, parents);
    }

    /// <summary>The columns of a new row, from its seeder, with the discriminators set from the placement.</summary>
    public async Task<IReadOnlyDictionary<string, object?>> Compose(
        TableUnderTest table,
        RowPlacement placement,
        IReadOnlyDictionary<string, SeededRow> parents)
    {
        var row = new Dictionary<string, object?>(await table.Seeder.Row(new SeedRow(table.Name, placement, parents, CreateUser)));

        if (table.Ownership.IsSpaceOwned)
        {
            row["space_id"] = placement.SpaceId;
        }

        if (table.Class is OwnershipClass.SpaceWithAudience)
        {
            row["audience_user_id"] = placement.Audience;
        }

        if (table.Class is OwnershipClass.User)
        {
            row["user_id"] = placement.User;
        }

        return row;
    }

    public async Task<IReadOnlyDictionary<string, object?>> Insert(
        TenantSession session,
        TableUnderTest table,
        IReadOnlyDictionary<string, object?> values)
    {
        var columns = values.Keys.ToList();
        var unknown = columns.Where(column => table.Shape.Column(column) is null).ToList();

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"The seeder for '{table.Name}' set unknown column(s): {string.Join(", ", unknown)}.");
        }

        var names = string.Join(", ", columns.Select(Quote));
        var placeholders = string.Join(", ", columns.Select((column, index) => $"@v{index}::{table.Shape.Column(column)!.Type}"));
        var returning = string.Join(", ", table.Shape.PrimaryKey.Select(Quote));
        var parameters = columns.Select((column, index) => ($"v{index}", values[column])).ToArray();
        return await session.Returning($"INSERT INTO {table.Quoted} ({names}) VALUES ({placeholders}) RETURNING {returning}", parameters);
    }

    public Task<long> CountRow(TenantSession session, TableUnderTest table, SeededRow row)
    {
        var (predicate, parameters) = KeyPredicate(table, row);
        return session.Count($"SELECT count(*) FROM {table.Quoted} WHERE {predicate}", parameters);
    }

    /// <summary>Updates the row to its own key, which changes nothing but still needs the right to update it.</summary>
    public Task<int> TouchRow(TenantSession session, TableUnderTest table, SeededRow row)
    {
        var first = Quote(table.Shape.PrimaryKey[0]);
        var (predicate, parameters) = KeyPredicate(table, row);
        return session.Execute($"UPDATE {table.Quoted} SET {first} = {first} WHERE {predicate}", parameters);
    }

    public Task<int> DeleteRow(TenantSession session, TableUnderTest table, SeededRow row)
    {
        var (predicate, parameters) = KeyPredicate(table, row);
        return session.Execute($"DELETE FROM {table.Quoted} WHERE {predicate}", parameters);
    }

    /// <summary>Sets one column of the row to <paramref name="value"/>.</summary>
    public Task<int> SetColumn(TenantSession session, TableUnderTest table, SeededRow row, string column, object value)
    {
        var (predicate, keyParameters) = KeyPredicate(table, row);
        return session.Execute(
            $"UPDATE {table.Quoted} SET {Quote(column)} = @value WHERE {predicate}",
            [.. keyParameters, ("value", value)]);
    }

    #endregion

    #region Private Methods

    private async Task<Guid> CreateUser()
    {
        var id = Guid.CreateVersion7();
        await using var session = await Open(null, id);
        session.Db.Users.Add(TenantWorld.NewUser(id));
        await session.Commit();
        return id;
    }

    private static (string Predicate, (string Name, object? Value)[] Parameters) KeyPredicate(TableUnderTest table, SeededRow row)
    {
        var columns = table.Shape.PrimaryKey;
        var predicate = string.Join(" AND ", columns.Select((column, index) => $"{Quote(column)} = @k{index}"));
        return (predicate, columns.Select((column, index) => ($"k{index}", row.Key[column])).ToArray());
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";

    #endregion
}
