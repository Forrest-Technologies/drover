namespace Drover.Data.Tests.Seeders;

/// <summary>A seeder that needs no waiting: it lists the columns of one valid row.</summary>
public abstract class PlainSeeder<TEntity> : TableSeeder<TEntity>
{
    #region Public Methods

    public sealed override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(Columns(seed));

    #endregion

    #region Protected Methods

    /// <summary>
    /// The columns of the row. Leave out what the database fills in, and the discriminators the suite sets itself.
    /// A column that points at a parent row must use <see cref="SeedRow.Parent"/>, so the suite can aim it at another
    /// space's row and see the database refuse.
    /// </summary>
    protected abstract Dictionary<string, object?> Columns(SeedRow seed);

    #endregion
}
