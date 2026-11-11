namespace Drover.Data.Tenancy;

/// <summary>
/// Sets the tenant as the first statement of every transaction. The third argument of <c>set_config</c> makes the
/// setting local to the transaction, so PostgreSQL discards it at commit or rollback and nothing travels with a
/// pooled connection.
/// </summary>
public sealed class TenantTransactionInterceptor(TenantContext tenant) : DbTransactionInterceptor
{
    #region Private Fields

    private const string SetTenantSql =
        "SELECT set_config('drover.space_id', @space, true), set_config('drover.user_id', @user, true)";

    #endregion

    #region Public Methods

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var command = BuildSetTenantCommand(connection, result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result) =>
        throw new NotSupportedException("Tenant transactions are opened asynchronously.");

    #endregion

    #region Private Methods

    private DbCommand BuildSetTenantCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SetTenantSql;
        command.Parameters.Add(new NpgsqlParameter("space", tenant.SpaceId?.ToString() ?? string.Empty));
        command.Parameters.Add(new NpgsqlParameter("user", tenant.UserId?.ToString() ?? string.Empty));
        return command;
    }

    #endregion
}
