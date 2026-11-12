namespace Drover.Data.Tenancy;

/// <summary>
/// Opens the one transaction in which tenant data may be read or written. Services open the scope; repositories
/// never open or commit one. Never hold it across a model call, a tool call, or any network wait.
/// </summary>
public sealed class TenantScope(DroverDbContext db)
{
    #region Public Methods

    public async Task<TenantWork> Begin(CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A TenantScope is already open. Scopes do not nest.");
        }

        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new TenantWork(db, transaction);
    }

    #endregion
}
