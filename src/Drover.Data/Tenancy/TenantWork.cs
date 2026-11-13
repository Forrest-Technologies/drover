namespace Drover.Data.Tenancy;

/// <summary>
/// One open tenant transaction. <see cref="Commit"/> saves tracked changes and commits; disposing without
/// committing rolls back.
/// </summary>
public sealed class TenantWork(DroverDbContext db, IDbContextTransaction transaction) : IAsyncDisposable
{
    #region Private Fields

    private bool committed;

    private bool disposed;

    #endregion

    #region Public Methods

    public async Task Commit(CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        committed = true;
    }

    // Not an async method: the caller's flow must see the scope as closed from here on (see TenantScope.Begin).
    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            TenantScope.MarkClosed();
        }

        return DisposeTransaction();
    }

    #endregion

    #region Private Methods

    private async ValueTask DisposeTransaction()
    {
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            if (!committed)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    #endregion
}
