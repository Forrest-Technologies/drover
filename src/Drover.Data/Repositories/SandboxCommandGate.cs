namespace Drover.Data.Repositories;

/// <summary>
/// The session lock that keeps a command and a tainted write from using one sandbox at once. It outlives the
/// transaction that decided the sandbox was clean, and the caller holds it until the provider call returns, then
/// releases it. No tenant transaction stays open across that call.
/// </summary>
public sealed class SandboxCommandGate : IAsyncDisposable
{
    #region Private Fields

    private readonly NpgsqlConnection connection;

    private readonly Guid sandboxId;

    private readonly long seed;

    private bool held;

    #endregion

    #region Constructors

    internal SandboxCommandGate(NpgsqlConnection connection, Guid sandboxId, long seed)
    {
        this.connection = connection;
        this.sandboxId = sandboxId;
        this.seed = seed;
    }

    #endregion

    #region Public Methods

    /// <summary>Releases the lock. Closing the connection would release it too, and this does it first.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!held)
        {
            await connection.DisposeAsync();
            return;
        }

        held = false;

        try
        {
            await Execute("SELECT pg_advisory_unlock(hashtextextended(@sandbox, @seed))", CancellationToken.None);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    #endregion

    #region Private Methods

    internal async Task Lock(CancellationToken cancellationToken)
    {
        await Execute("SELECT pg_advisory_lock(hashtextextended(@sandbox, @seed))", cancellationToken);
        held = true;
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
