namespace Drover.Data.Tests.Isolation;

/// <summary>
/// Knows how to make one valid row of a table. Every entity that belongs to a space or a user registers one by
/// deriving from <see cref="TableSeeder{TEntity}"/>; the isolation suite finds them by reflection and runs its
/// generated tests with them. An entity without a seeder fails <c>SeederCoverageTests</c>.
/// </summary>
public abstract class TableSeeder
{
    #region Properties

    public abstract Type Entity { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// The columns of the row, by column name. Leave out what the database fills in. Leave out <c>space_id</c>,
    /// <c>audience_user_id</c>, and <c>user_id</c> too when they are the table's discriminators: the suite sets those
    /// itself, so a seeder cannot get them wrong.
    /// </summary>
    public abstract Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed);

    #endregion
}

public abstract class TableSeeder<TEntity> : TableSeeder
{
    public sealed override Type Entity => typeof(TEntity);
}

/// <summary>Where a row is placed: whose space, which audience, and which user acts for it or owns it.</summary>
public sealed record RowPlacement(Guid? SpaceId, Guid Audience, Guid User)
{
    /// <summary>A row any member of the space may read, made by <paramref name="user"/>.</summary>
    public static RowPlacement Shared(Guid spaceId, Guid user) => new(spaceId, Domain.Tenancy.Audience.Everyone, user);

    /// <summary>A row only <paramref name="user"/> may read.</summary>
    public static RowPlacement Private(Guid spaceId, Guid user) => new(spaceId, user, user);

    /// <summary>A row owned by one user, whatever space they are in.</summary>
    public static RowPlacement OwnedBy(Guid user) => new(null, Domain.Tenancy.Audience.Everyone, user);
}

/// <summary>A row that was inserted: its table, its key, where it was placed, and the parent rows it points at.</summary>
public sealed record SeededRow(
    string Table,
    IReadOnlyDictionary<string, object?> Key,
    RowPlacement Placement,
    IReadOnlyDictionary<string, SeededRow> Parents);

/// <summary>What a seeder is given: the row's placement, its parent rows, and a way to make a user.</summary>
public sealed class SeedRow(
    string table,
    RowPlacement placement,
    IReadOnlyDictionary<string, SeededRow> parents,
    Func<Task<Guid>> createUser)
{
    #region Properties

    public string Table { get; } = table;

    public RowPlacement Placement { get; } = placement;

    #endregion

    #region Public Methods

    /// <summary>A value for a column that must be unique, such as a name or a key.</summary>
    public string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>A key column of the parent row in <paramref name="parentTable"/> this row points at.</summary>
    public object? Parent(string parentTable, string column = "id") =>
        parents.TryGetValue(parentTable, out var parent)
            ? parent.Key[column]
            : throw new InvalidOperationException($"'{Table}' has no parent row in '{parentTable}'.");

    /// <summary>A new user who exists in the database, for a row that needs one of its own.</summary>
    public Task<Guid> CreateUser() => createUser();

    #endregion
}
