namespace Drover.Data.Repositories;

/// <summary>
/// Stores workflow checkpoints as opaque JSON. The <c>workflow_checkpoints</c> table is a Phase 1 migration; until
/// then only the agent framework proofs create it, in their own test database. Runs inside the caller's tenant
/// scope. The table is space-owned with an audience, like <c>agent_sessions</c>: its policies limit it to the current
/// space and to the run's audience. The state is
/// written as <c>json</c>, never <c>jsonb</c>: the framework's polymorphic JSON needs its "$type" property kept first,
/// and jsonb reorders object keys.
/// </summary>
public sealed class WorkflowCheckpointRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task AddCheckpoint(
        Guid audienceUserId,
        string sessionId,
        string checkpointId,
        string? parentCheckpointId,
        JsonElement state,
        CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO workflow_checkpoints (audience_user_id, session_id, checkpoint_id, parent_checkpoint_id, state)
            VALUES ({audienceUserId}, {sessionId}, {checkpointId}, {parentCheckpointId}, {state.GetRawText()}::json)
            """,
            cancellationToken);

    public async Task<JsonElement?> GetCheckpoint(
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken)
    {
        var state = await db.Database
            .SqlQuery<string>(
                $"""
                SELECT state::text AS "Value" FROM workflow_checkpoints
                WHERE session_id = {sessionId} AND checkpoint_id = {checkpointId}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        return state is null ? null : JsonDocument.Parse(state).RootElement.Clone();
    }

    public async Task<IReadOnlyList<string>> ListCheckpointIds(
        string sessionId,
        string? parentCheckpointId,
        CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<string>(
                $"""
                SELECT checkpoint_id AS "Value" FROM workflow_checkpoints
                WHERE session_id = {sessionId}
                  AND ({parentCheckpointId}::text IS NULL OR parent_checkpoint_id = {parentCheckpointId})
                ORDER BY created_at, checkpoint_id
                """)
            .ToListAsync(cancellationToken);

    #endregion
}
