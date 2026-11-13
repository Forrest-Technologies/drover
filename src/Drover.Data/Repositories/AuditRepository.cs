namespace Drover.Data.Repositories;

/// <summary>
/// The audit log. Rows are added and read, never changed: the application role has no update or delete on the table.
/// </summary>
public sealed class AuditRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(AuditLogEntry entry) => db.AuditLog.Add(entry);

    public Task<List<AuditLogEntry>> ListEntriesAbout(Guid targetId, CancellationToken cancellationToken) =>
        db.AuditLog.AsNoTracking()
            .Where(entry => entry.TargetId == targetId)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    #endregion
}
