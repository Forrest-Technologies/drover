namespace Drover.Tests.Common;

/// <summary>
/// Holds exclusive use of the server-wide test roles while one test assembly uses them. Test projects can still be
/// launched together against one PostgreSQL server, but their independently generated role passwords cannot replace
/// one another while connections are active.
/// </summary>
internal sealed class PostgresRoleLease(NpgsqlConnection connection) : IAsyncDisposable
{
    #region Private Fields

    private const long AdvisoryLockKey = 0x44524F56_45520043;

    #endregion

    #region Public Methods

    public static async Task<PostgresRoleLease> Acquire(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection)
            {
                CommandTimeout = 0,
            };
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            _ = await command.ExecuteScalarAsync(timeout.Token);
            return new PostgresRoleLease(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await using (connection)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            _ = await command.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    #endregion
}
