namespace Drover.Data.Repositories;

/// <summary>
/// Model connections and their catalog. Every method runs inside a tenant scope the service has opened. Reads that
/// are used after the scope closes are untracked, so a later scope reloads the row instead of reusing a stale one.
/// </summary>
public sealed class ModelConnectionRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(ModelConnection connection) => db.ModelConnections.Add(connection);

    public void Add(ModelCatalogEntry entry) => db.ModelCatalog.Add(entry);

    /// <summary>The tracked row, for a change the caller commits. Null when it does not exist in this space.</summary>
    public Task<ModelConnection?> GetConnection(Guid id, CancellationToken cancellationToken) =>
        db.ModelConnections.SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken);

    public Task<ModelConnection?> FindConnection(Guid id, CancellationToken cancellationToken) =>
        db.ModelConnections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken);

    public Task<List<ModelConnection>> ListConnections(CancellationToken cancellationToken) =>
        db.ModelConnections.AsNoTracking().OrderBy(connection => connection.Name).ThenBy(connection => connection.Id)
            .ToListAsync(cancellationToken);

    public Task<List<ModelCatalogEntry>> ListCatalog(Guid connectionId, CancellationToken cancellationToken) =>
        db.ModelCatalog.AsNoTracking()
            .Where(entry => entry.ModelConnectionId == connectionId)
            .OrderBy(entry => entry.DisplayName).ThenBy(entry => entry.Model)
            .ToListAsync(cancellationToken);

    /// <summary>The tracked catalog rows of one connection, for the upsert.</summary>
    public Task<List<ModelCatalogEntry>> GetCatalog(Guid connectionId, CancellationToken cancellationToken) =>
        db.ModelCatalog.Where(entry => entry.ModelConnectionId == connectionId).ToListAsync(cancellationToken);

    /// <summary>
    /// True when a save failed because another request stored the same model first. The transaction is lost, so the
    /// caller starts a new scope, reads again, and updates the row instead of adding it.
    /// </summary>
    public static bool IsAlreadyStored(Exception failure) =>
        failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    #endregion
}
