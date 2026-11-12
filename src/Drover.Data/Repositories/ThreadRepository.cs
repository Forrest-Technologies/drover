namespace Drover.Data.Repositories;

/// <summary>
/// Threads as rows. Row-level security shows a person their own threads and the shared ones, so nothing here filters
/// by audience. Runs inside the caller's tenant scope.
/// </summary>
public sealed class ThreadRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(ChatThread thread) => db.Threads.Add(thread);

    public Task<ChatThread?> FindThread(Guid id, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking().SingleOrDefaultAsync(thread => thread.Id == id, cancellationToken);

    /// <summary>
    /// The tracked row, locked until the transaction ends. Posting a message takes this lock first, so two posts to
    /// one thread take turns: positions stay in order and a thread never gets two active runs.
    /// </summary>
    public async Task<ChatThread?> GetThreadForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Threads.FromSql($"SELECT * FROM threads WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>The threads with one bot that the caller may see and has not archived, newest activity first.</summary>
    public Task<List<ChatThread>> ListThreadsForBot(Guid botId, int limit, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.BotId == botId && thread.ArchivedAt == null)
            .OrderByDescending(thread => thread.LastActivityAt)
            .ThenByDescending(thread => thread.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    #endregion
}
