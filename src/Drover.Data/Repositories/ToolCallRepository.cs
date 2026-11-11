namespace Drover.Data.Repositories;

/// <summary>Stores tool calls as rows, for the activity view, the approval inbox, and restart recovery.</summary>
public sealed class ToolCallRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<ToolCall?> GetToolCall(Guid id, CancellationToken cancellationToken) =>
        await db.ToolCalls.SingleOrDefaultAsync(call => call.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ToolCall>> ListToolCalls(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await db.ToolCalls.AsNoTracking().Where(call => ids.Contains(call.Id)).ToListAsync(cancellationToken);

    public void AddToolCall(ToolCall call) => db.ToolCalls.Add(call);

    #endregion
}
