namespace Drover.Data.Repositories;

/// <summary>Stores tool calls as rows, for the activity view, the approval inbox, and restart recovery.</summary>
public sealed class ToolCallRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<ToolCall?> GetToolCall(Guid id, CancellationToken cancellationToken) =>
        await db.ToolCalls.SingleOrDefaultAsync(call => call.Id == id, cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so two answers to one approval take turns.</summary>
    public async Task<ToolCall?> GetToolCallForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.ToolCalls.FromSql($"SELECT * FROM tool_calls WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<ToolCall>> ListToolCalls(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await db.ToolCalls.AsNoTracking().Where(call => ids.Contains(call.Id)).ToListAsync(cancellationToken);

    /// <summary>The thread's calls still waiting for a human, tracked so the caller can settle them.</summary>
    public async Task<IReadOnlyList<ToolCall>> ListPendingApprovals(Guid threadId, CancellationToken cancellationToken) =>
        await db.ToolCalls
            .Where(call => call.ThreadId == threadId && call.Status == ToolCallStatus.PendingApproval)
            .ToListAsync(cancellationToken);

    /// <summary>The run's calls that started and have no outcome: after a restart, the ones a kill cut off.</summary>
    public async Task<IReadOnlyList<ToolCall>> ListRunningCalls(Guid runId, CancellationToken cancellationToken) =>
        await db.ToolCalls.AsNoTracking()
            .Where(call => call.RunId == runId && call.Status == ToolCallStatus.Running && call.StartedAt != null)
            .ToListAsync(cancellationToken);

    public void AddToolCall(ToolCall call) => db.ToolCalls.Add(call);

    #endregion
}
