namespace Drover.Data.Repositories;

/// <summary>
/// The lock that lets one refresh of a person's model sign-in run at a time, in this process and in any other on the
/// same database. A refresh token is spent by its first use, so two runs that both refresh would end the sign-in.
/// The lock is a session advisory lock on a connection of its own, held while the provider is asked: it reads and
/// writes no row, and no tenant transaction stays open while it is waited on or held. The caller reads the
/// credential again in a short tenant transaction once it holds the lock, and writes the new tokens before letting go.
/// </summary>
public sealed class ModelCredentialLocks(string connectionString, ILogger<ModelCredentialLocks> logger)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same id.
    private const long RefreshLockSeed = 0x44524F56_4552000C;

    #endregion

    #region Public Methods

    /// <summary>Waits for the credential's refresh lock and holds it until the result is disposed.</summary>
    public async Task<IAsyncDisposable> Hold(Guid credentialId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            await Execute(connection, "SELECT pg_advisory_lock(hashtextextended(@credential, @seed))", credentialId, cancellationToken);
            return new Held(connection, credentialId, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    #endregion

    #region Private Methods

    private static async Task Execute(NpgsqlConnection connection, string sql, Guid credentialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("credential", credentialId.ToString());
        command.Parameters.AddWithValue("seed", RefreshLockSeed);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Releases the lock and closes its connection. A lock that cannot be released, because the connection dropped
    /// meanwhile, is released by closing the connection.
    /// </summary>
    private sealed class Held(NpgsqlConnection connection, Guid credentialId, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Execute(connection, "SELECT pg_advisory_unlock(hashtextextended(@credential, @seed))", credentialId, CancellationToken.None);
            }
            catch (Exception failure) when (failure is NpgsqlException or InvalidOperationException or IOException)
            {
                logger.LogWarning(failure, "A model sign-in refresh lock could not be released; closing its connection releases it");
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    #endregion
}
