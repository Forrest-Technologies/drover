namespace Drover.Data.Repositories;

/// <summary>Stores the agent framework's serialized session per thread and bot. Runs inside the caller's tenant scope.</summary>
public sealed class AgentSessionRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<AgentSession?> GetSession(Guid threadId, Guid botId, CancellationToken cancellationToken) =>
        await db.AgentSessions.SingleOrDefaultAsync(
            session => session.ThreadId == threadId && session.BotId == botId,
            cancellationToken);

    /// <summary>Replaces the stored session, or adds one when the thread has none for this bot.</summary>
    public async Task SaveSession(
        ChatThread thread,
        Guid botId,
        JsonElement state,
        string frameworkVersion,
        CancellationToken cancellationToken)
    {
        var session = await GetSession(thread.Id, botId, cancellationToken);

        if (session is null)
        {
            session = new AgentSession
            {
                ThreadId = thread.Id,
                AudienceUserId = thread.AudienceUserId,
                BotId = botId,
                FrameworkVersion = frameworkVersion,
            };
            db.AgentSessions.Add(session);
        }

        session.State = state;
        session.FrameworkVersion = frameworkVersion;
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task DeleteSession(Guid threadId, Guid botId, CancellationToken cancellationToken) =>
        await db.AgentSessions
            .Where(session => session.ThreadId == threadId && session.BotId == botId)
            .ExecuteDeleteAsync(cancellationToken);

    #endregion
}
