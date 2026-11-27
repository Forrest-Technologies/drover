namespace Drover.Data.Repositories;

/// <summary>One pending memory forget request found across spaces: ids only, never tenant content.</summary>
public sealed record MemoryForgetDue(Guid SpaceId, Guid RequestId);


/// <summary>
/// Durable forget requests for the external memory service. Tenant operations run inside the caller's tenant scope;
/// cross-space discovery goes through <c>sys.memory_forget_requests_due</c> and returns ids only.
/// </summary>
public sealed class MemoryForgetRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(MemoryForgetRequest request) => db.MemoryForgetRequests.Add(request);

    public async Task<MemoryForgetRequest?> GetForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.MemoryForgetRequests
            .FromSql($"SELECT * FROM memory_forget_requests WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task RemoveById(Guid id, CancellationToken cancellationToken) =>
        db.MemoryForgetRequests
            .Where(request => request.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Pending forget requests across all spaces that have not been attempted since <paramref name="notTriedSince"/>,
    /// taking turns across spaces so one space cannot starve others. Returns ids only.
    /// </summary>
    public Task<List<MemoryForgetDue>> ListDue(DateTimeOffset notTriedSince, int maxRows, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<MemoryForgetDue>(
                $"SELECT space_id, request_id FROM sys.memory_forget_requests_due({notTriedSince}, {maxRows})")
            .ToListAsync(cancellationToken);

    #endregion
}
