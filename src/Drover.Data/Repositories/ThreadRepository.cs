namespace Drover.Data.Repositories;

/// <summary>
/// What one thread holds for one person: their newest run in it, whether a call in it still waits for an approval,
/// when they last opened it, and whether a bot said something in a run of theirs after the last message they saw.
/// </summary>
public sealed record ThreadFacts(Run? NewestRun, bool ApprovalOpen, DateTimeOffset? SeenAt, bool HasNew);

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

    #region Seen

    /// <summary>
    /// Marks the thread seen by the person as far as its newest message, now. A marker never moves back: two screens
    /// marking at once keep the further one. The marker takes the thread's audience, which a later change of the
    /// thread's audience carries along.
    /// </summary>
    public Task MarkSeen(ChatThread thread, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO thread_reads (space_id, thread_id, user_id, audience_user_id, seen_through_position, seen_at)
            VALUES (
              sys.current_space(), {thread.Id}, {userId}, {thread.AudienceUserId},
              COALESCE((SELECT max(position) FROM messages WHERE thread_id = {thread.Id}), 0),
              {now})
            ON CONFLICT (space_id, thread_id, user_id) DO UPDATE
            SET seen_through_position = GREATEST(thread_reads.seen_through_position, EXCLUDED.seen_through_position),
                seen_at = GREATEST(thread_reads.seen_at, EXCLUDED.seen_at)
            """,
            cancellationToken);

    /// <summary>
    /// What each of the threads holds for the person, from their own runs and their own marker only. Runs have no
    /// audience, so another member's runs in a shared thread are visible here; they are left out by name.
    /// </summary>
    public async Task<Dictionary<Guid, ThreadFacts>> ListFacts(
        IReadOnlyCollection<Guid> threadIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ids = threadIds.ToArray();
        var newest = await db.Runs
            .FromSql(
                $"""
                SELECT DISTINCT ON (thread_id) * FROM runs
                WHERE thread_id = ANY({ids}) AND principal_user_id = {userId}
                ORDER BY thread_id, id DESC
                """)
            .AsNoTracking()
            .ToDictionaryAsync(run => run.ThreadId, cancellationToken);
        var open = (await db.ToolCalls.AsNoTracking()
            .Where(call => ids.Contains(call.ThreadId) && call.Status == ToolCallStatus.PendingApproval)
            .Where(call => db.Runs.Any(run => run.Id == call.RunId && run.PrincipalUserId == userId))
            .Select(call => call.ThreadId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var seen = await db.ThreadReads.AsNoTracking()
            .Where(read => ids.Contains(read.ThreadId) && read.UserId == userId)
            .ToDictionaryAsync(read => read.ThreadId, read => read.SeenAt, cancellationToken);
        var withNew = (await db.Messages.AsNoTracking()
            .Where(message => ids.Contains(message.ThreadId) && message.Role == MessageRole.Assistant && message.Position != null)
            .Where(message => db.Runs.Any(run => run.Id == message.RunId && run.PrincipalUserId == userId))
            .Where(message => message.Position > (db.ThreadReads
                .Where(read => read.ThreadId == message.ThreadId && read.UserId == userId)
                .Select(read => (int?)read.SeenThroughPosition)
                .FirstOrDefault() ?? 0))
            .Select(message => message.ThreadId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        return ids.Distinct().ToDictionary(
            id => id,
            id => new ThreadFacts(
                newest.GetValueOrDefault(id),
                open.Contains(id),
                seen.TryGetValue(id, out var at) ? at : null,
                withNew.Contains(id)));
    }

    #endregion
}
