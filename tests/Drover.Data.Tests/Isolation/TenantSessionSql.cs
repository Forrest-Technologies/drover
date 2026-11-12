namespace Drover.Data.Tests.Isolation;

/// <summary>
/// Plain SQL inside a session's tenant transaction. The generated tests use it because they work from table and
/// column names read from the catalog, not from entity types.
/// </summary>
public static class TenantSessionSql
{
    #region Public Methods

    public static async Task<int> Execute(
        this TenantSession session,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = NewCommand(session, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> Count(
        this TenantSession session,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = NewCommand(session, sql, parameters);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs an <c>INSERT ... RETURNING</c> and reads the returned columns of the one row.</summary>
    public static async Task<IReadOnlyDictionary<string, object?>> Returning(
        this TenantSession session,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = NewCommand(session, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var row = new Dictionary<string, object?>();

        if (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
            }
        }

        return row;
    }

    #endregion

    #region Private Methods

    private static NpgsqlCommand NewCommand(TenantSession session, string sql, (string Name, object? Value)[] parameters)
    {
        var database = session.Db.Database;
        var command = (NpgsqlCommand)database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = (NpgsqlTransaction)database.CurrentTransaction!.GetDbTransaction();

        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        }

        return command;
    }

    #endregion
}
