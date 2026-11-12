namespace Drover.Data.Tenancy;

/// <summary>
/// Throws when a command runs with no transaction. Without one the tenant is not set, so the query would return
/// zero rows; failing loudly turns a forgotten <see cref="TenantScope"/> into an error in development.
/// </summary>
public sealed class TenantGuardInterceptor : DbCommandInterceptor
{
    #region Private Fields

    private const string OutsideScopeMessage =
        "Tenant data was queried outside a tenant transaction. "
        + "Open a TenantScope with TenantScope.Begin and run the query inside it.";

    #endregion

    #region Public Methods

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        RequireTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        RequireTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        RequireTransaction(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(command);
        return ValueTask.FromResult(result);
    }

    #endregion

    #region Private Methods

    private static void RequireTransaction(DbCommand command)
    {
        if (command.Transaction is null)
        {
            throw new InvalidOperationException(OutsideScopeMessage);
        }
    }

    #endregion
}
