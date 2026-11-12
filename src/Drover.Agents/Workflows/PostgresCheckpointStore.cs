namespace Drover.Agents.Workflows;

/// <summary>
/// Workflow checkpoints as opaque JSON in PostgreSQL, inside a tenant transaction, so a team run resumes in any
/// process from its latest checkpoint. A team run's checkpoints carry the audience of the thread it belongs to, so a
/// private thread's team state is as private as its messages. The table arrives with teams in Phase 1.
/// </summary>
public sealed class PostgresCheckpointStore(RunDatabase database, Guid audienceUserId) : JsonCheckpointStore
{
    #region Public Methods

    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent = null)
    {
        var checkpoint = new CheckpointInfo(sessionId, Guid.CreateVersion7().ToString("N"));
        await database.Transact(
            services => services.GetRequiredService<WorkflowCheckpointRepository>()
                .AddCheckpoint(audienceUserId, sessionId, checkpoint.CheckpointId, parent?.CheckpointId, value, CancellationToken.None),
            CancellationToken.None);
        return checkpoint;
    }

    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        var state = await database.Transact(
            services => services.GetRequiredService<WorkflowCheckpointRepository>()
                .GetCheckpoint(sessionId, key.CheckpointId, CancellationToken.None),
            CancellationToken.None);

        return state ?? throw new KeyNotFoundException($"Checkpoint {key.CheckpointId} of session {sessionId} does not exist.");
    }

    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null)
    {
        var ids = await database.Transact(
            services => services.GetRequiredService<WorkflowCheckpointRepository>()
                .ListCheckpointIds(sessionId, withParent?.CheckpointId, CancellationToken.None),
            CancellationToken.None);

        return ids.Select(id => new CheckpointInfo(sessionId, id));
    }

    #endregion
}
