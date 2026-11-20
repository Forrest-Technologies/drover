namespace Drover.Data.Repositories;

/// <summary>A command could not be admitted to a sandbox because too many are already holding or waiting for it.</summary>
public sealed class SandboxBusyException(string message) : Exception(message);

/// <summary>How many commands may hold, or wait for, a sandbox's command lock at once.</summary>
public sealed record SandboxCommandLimits
{
    #region Properties

    /// <summary>Command locks held at once, in the whole server process. Each holds one database session.</summary>
    public int MaxHeld { get; init; } = 24;

    /// <summary>Command locks one space holds at once, so one space cannot take every session.</summary>
    public int MaxHeldPerSpace { get; init; } = 8;

    /// <summary>The holder and the waiters of one sandbox: a model may call several tools at once. A waiter holds no database session.</summary>
    public int MaxQueuedPerSandbox { get; init; } = 16;

    /// <summary>How long a waiter queues behind the holder of its sandbox before it is refused.</summary>
    public TimeSpan WaitForTurn { get; init; } = TimeSpan.FromSeconds(30);

    #endregion
}

/// <summary>
/// The lock that keeps a command and a tainted write from using one sandbox at once. A caller first takes a turn in
/// this process, which costs no database session and is bounded, and only then opens the one session that holds the
/// advisory lock, so the sessions are never more than <see cref="SandboxCommandLimits.MaxHeld"/> and none is spent on
/// a waiter. The advisory lock itself still keeps other server processes out. No tenant transaction stays open while
/// either is waited on or held.
/// </summary>
public sealed class SandboxCommandLocks(
    string connectionString,
    ILogger<SandboxCommandLocks> logger,
    SandboxCommandLimits? limits = null)
{
    #region Private Fields

    // A different key from the copy fence (0x07): one sandbox's command admission, held on its own connection
    // until the provider call returns.
    private const long CommandLockSeed = 0x44524F56_45520008;

    private readonly SandboxCommandLimits bounds = limits ?? new();

    private readonly object sync = new();

    private readonly Dictionary<Guid, SandboxTurn> turns = [];

    private readonly Dictionary<Guid, int> heldPerSpace = [];

    private int held;

    #endregion

    #region Public Methods

    /// <summary>
    /// Takes the sandbox's command lock. The caller disposes the gate when the provider call is over. Throws
    /// <see cref="SandboxBusyException"/> when the bounds are reached, before any database session is opened.
    /// </summary>
    public async Task<SandboxCommandGate> Hold(Guid spaceId, Guid sandboxId, CancellationToken cancellationToken)
    {
        var turn = await TakeTurn(sandboxId, cancellationToken);

        try
        {
            ReserveSession(spaceId);
        }
        catch
        {
            EndTurn(sandboxId, turn);
            throw;
        }

        var release = () => Release(spaceId, sandboxId, turn);

        try
        {
            return await OpenGate(sandboxId, release, cancellationToken);
        }
        catch
        {
            release();
            throw;
        }
    }

    #endregion

    #region Private Methods

    private async Task<SandboxTurn> TakeTurn(Guid sandboxId, CancellationToken cancellationToken)
    {
        SandboxTurn turn;
        lock (sync)
        {
            turn = turns.TryGetValue(sandboxId, out var existing) ? existing : turns[sandboxId] = new();
            if (turn.Queued >= bounds.MaxQueuedPerSandbox)
            {
                throw Busy("Too many commands are waiting for this sandbox. Try again in a moment.");
            }

            turn.Queued++;
        }

        try
        {
            if (!await turn.Semaphore.WaitAsync(bounds.WaitForTurn, cancellationToken))
            {
                throw Busy("This sandbox is still busy with another command. Try again in a moment.");
            }
        }
        catch
        {
            EndTurn(sandboxId, turn);
            throw;
        }

        return turn;
    }

    private void ReserveSession(Guid spaceId)
    {
        lock (sync)
        {
            var space = heldPerSpace.GetValueOrDefault(spaceId);
            if (held >= bounds.MaxHeld || space >= bounds.MaxHeldPerSpace)
            {
                throw Busy("The server is running as many sandbox commands as it allows. Try again in a moment.");
            }

            held++;
            heldPerSpace[spaceId] = space + 1;
        }
    }

    private async Task<SandboxCommandGate> OpenGate(Guid sandboxId, Action release, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        var gate = new SandboxCommandGate(connection, sandboxId, CommandLockSeed, release, logger);

        try
        {
            await connection.OpenAsync(cancellationToken);
            await gate.Lock(cancellationToken);
            return gate;
        }
        catch
        {
            await gate.DisposeAsync();
            throw;
        }
    }

    private void Release(Guid spaceId, Guid sandboxId, SandboxTurn turn)
    {
        lock (sync)
        {
            held--;
            if (heldPerSpace[spaceId] <= 1)
            {
                heldPerSpace.Remove(spaceId);
            }
            else
            {
                heldPerSpace[spaceId]--;
            }
        }

        turn.Semaphore.Release();
        EndTurn(sandboxId, turn);
    }

    private void EndTurn(Guid sandboxId, SandboxTurn turn)
    {
        lock (sync)
        {
            if (--turn.Queued == 0)
            {
                turns.Remove(sandboxId);
            }
        }
    }

    private SandboxBusyException Busy(string message)
    {
        logger.LogWarning("A sandbox command was refused because the command locks are at their bounds");
        return new(message);
    }

    #endregion

    #region Nested Types

    private sealed class SandboxTurn
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Queued { get; set; }
    }

    #endregion
}
