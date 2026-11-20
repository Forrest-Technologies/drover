namespace Drover.Data.Repositories;

/// <summary>
/// The session lock that keeps a command and a tainted write from using one sandbox at once. It outlives the
/// transaction that decided the sandbox was clean, and the caller holds it until the provider call returns, then
/// releases it. No tenant transaction stays open across that call. <see cref="SandboxCommandLocks"/> makes it.
/// </summary>
public sealed class SandboxCommandGate : IAsyncDisposable
{
    #region Private Fields

    private readonly NpgsqlConnection connection;

    private readonly Guid sandboxId;

    private readonly long seed;

    private readonly Action release;

    private readonly ILogger logger;

    private bool held;

    private bool disposed;

    #endregion

    #region Constructors

    internal SandboxCommandGate(NpgsqlConnection connection, Guid sandboxId, long seed, Action release, ILogger logger)
    {
        this.connection = connection;
        this.sandboxId = sandboxId;
        this.seed = seed;
        this.release = release;
        this.logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Releases the lock and the room it took, and closes the connection. A lock that cannot be released, because the
    /// connection dropped while the provider call ran, is released by closing the connection, so that is not an error
    /// for a command that already ran.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            if (held)
            {
                held = false;
                await Unlock();
            }
        }
        finally
        {
            await connection.DisposeAsync();
            release();
        }
    }

    #endregion

    #region Private Methods

    internal async Task Lock(CancellationToken cancellationToken)
    {
        await Execute("SELECT pg_advisory_lock(hashtextextended(@sandbox, @seed))", cancellationToken);
        held = true;
    }

    private async Task Unlock()
    {
        try
        {
            await Execute("SELECT pg_advisory_unlock(hashtextextended(@sandbox, @seed))", CancellationToken.None);
        }
        catch (Exception failure) when (failure is NpgsqlException or InvalidOperationException or IOException)
        {
            logger.LogWarning(failure, "A sandbox command lock could not be released; closing its connection releases it");
        }
    }

    private async Task Execute(string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("sandbox", sandboxId.ToString());
        command.Parameters.AddWithValue("seed", seed);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion
}
