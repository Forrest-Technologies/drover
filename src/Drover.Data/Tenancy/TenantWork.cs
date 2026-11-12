namespace Drover.Data.Tenancy;

/// <summary>
/// One open tenant transaction. <see cref="Commit"/> saves tracked changes and commits; disposing without
/// committing rolls back.
/// </summary>
public sealed class TenantWork(DroverDbContext db, IDbContextTransaction transaction) : IAsyncDisposable
{
    #region Private Fields

    private bool committed;

    #endregion

    #region Public Methods

    public async Task Commit(CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        committed = true;
    }

    public async ValueTask DisposeAsync()
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
