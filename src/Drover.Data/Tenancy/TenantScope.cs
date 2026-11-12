namespace Drover.Data.Tenancy;

/// <summary>
/// Opens the one transaction in which tenant data may be read or written. Services open the scope; repositories
/// never open or commit one. Never hold it across a model call, a tool call, or any network wait.
/// </summary>
public sealed class TenantScope(DroverDbContext db)
{
    #region Private Fields

    private static readonly AsyncLocal<int> openInFlow = new();

    #endregion

    #region Properties

    /// <summary>
    /// True while the code asking is inside a tenant transaction: between <see cref="Begin"/> and the disposal of the
    /// work it returned, in the same flow of execution. Code about to wait on a model, a tool, or the network checks
    /// it and refuses to go on.
    /// </summary>
    public static bool IsOpenInCurrentFlow => openInFlow.Value > 0;

    #endregion

    #region Public Methods

    // Not an async method on purpose: an async method's changes to an AsyncLocal are undone when it returns, and the
    // caller must see this one for as long as the transaction is open.
    public Task<TenantWork> Begin(CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A TenantScope is already open. Scopes do not nest.");
        }

        openInFlow.Value++;
        return BeginTransaction(cancellationToken);
    }

    #endregion

    #region Private Methods

    internal static void MarkClosed() => openInFlow.Value = Math.Max(0, openInFlow.Value - 1);

    private async Task<TenantWork> BeginTransaction(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new TenantWork(db, transaction);
    }

    #endregion
}
