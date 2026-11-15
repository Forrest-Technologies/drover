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

    /// <summary>The person's main thread with the bot, or null when they have none yet.</summary>
    public Task<ChatThread?> FindMainThread(Guid botId, Guid ownerUserId, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .SingleOrDefaultAsync(
                thread => thread.BotId == botId && thread.OwnerUserId == ownerUserId && thread.IsMain,
                cancellationToken);

    /// <summary>
    /// Stores a new private chat thread as its owner's main thread with its bot, unless they already have one, and
    /// says whether it was stored. When two requests race, the unique index lets one in; the other waits for it and
    /// stores nothing without failing its transaction, so it can read the winner's row. The space is the
    /// transaction's own.
    /// </summary>
    public async Task<bool> AddMainThread(Guid botId, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO threads (id, space_id, bot_id, owner_user_id, audience_user_id, origin, last_activity_at, is_main)
            VALUES ({Guid.CreateVersion7()}, sys.current_space(), {botId}, {ownerUserId}, {ownerUserId}, 'chat', {now}, true)
            ON CONFLICT (space_id, bot_id, owner_user_id) WHERE is_main DO NOTHING
            """,
            cancellationToken) == 1;

    /// <summary>
    /// Replaces a main thread's summary, text and reach together in one statement, and says whether it did. It does
    /// so only while the summary still reaches exactly as far as <paramref name="previousThrough"/> and the new one
    /// reaches further: a summary written meanwhile by another worker is never overwritten by an older one.
    /// </summary>
    public async Task<bool> ReplaceSummary(
        Guid threadId,
        int? previousThrough,
        string summary,
        int throughPosition,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throughPosition > (previousThrough ?? 0)
        && await db.Threads
            .Where(thread => thread.Id == threadId && thread.IsMain && thread.SummaryThroughPosition == previousThrough)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(thread => thread.Summary, summary)
                    .SetProperty(thread => thread.SummaryThroughPosition, throughPosition)
                    .SetProperty(thread => thread.SummaryUpdatedAt, now),
                cancellationToken) == 1;

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
