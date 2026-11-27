namespace Drover.Data.Repositories;

/// <summary>
/// What one thread holds for one person: their newest root run in it (the run a message started, never a run another
/// bot handed work to), whether a call anywhere in that run's tree still waits for an approval, when they last opened
/// the thread, and whether a bot said something in a run of theirs after the last message they saw.
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

    /// <summary>
    /// Deletes a thread and every conversation-owned child row that references it, inside the caller's tenant scope.
    /// </summary>
    public async Task Delete(Guid threadId, CancellationToken cancellationToken)
    {
        await db.AgentSessions.Where(session => session.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.ToolCalls.Where(call => call.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.RunEvents.Where(stored => stored.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.Files.Where(file => file.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.Messages
            .Where(message => message.ThreadId == threadId && message.ParentId != null)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.ParentId, (Guid?)null), cancellationToken);
        await db.Messages.Where(message => message.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.ThreadReads.Where(read => read.ThreadId == threadId).ExecuteDeleteAsync(cancellationToken);
        await db.Threads.Where(thread => thread.Id == threadId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Makes a shared thread private to its owner. Child rows follow, because their foreign key updates with this
    /// one. False when the thread is missing or already private.
    /// </summary>
    public async Task<bool> MakePrivate(Guid threadId, Guid ownerUserId, CancellationToken cancellationToken) =>
        await db.Threads
            .Where(thread => thread.Id == threadId && thread.OwnerUserId == ownerUserId && thread.AudienceUserId == Audience.Everyone)
            .ExecuteUpdateAsync(set => set.SetProperty(thread => thread.AudienceUserId, ownerUserId), cancellationToken) == 1;

    public Task<ChatThread?> FindThread(Guid id, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking().SingleOrDefaultAsync(thread => thread.Id == id, cancellationToken);

    /// <summary>
    /// The tracked row, locked until the transaction ends. Posting a message takes this lock first, so two posts to
    /// one thread take turns: positions stay in order and a thread never gets two active runs.
    /// </summary>
    public async Task<ChatThread?> GetThreadForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Threads.FromSql($"SELECT * FROM threads WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>
    /// Locks a thread against message posting without blocking an upload's foreign-key check. A locked thread is
    /// skipped so one busy conversation cannot hold a cleanup pass.
    /// </summary>
    public async Task<ChatThread?> GetThreadForUploadCleanup(Guid id, CancellationToken cancellationToken) =>
        (await db.Threads
            .FromSql($"SELECT * FROM threads WHERE id = {id} FOR NO KEY UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>
    /// Locks a thread against message posting while a person removes an unsent file. The weaker lock stays
    /// compatible with a concurrent upload's foreign-key check.
    /// </summary>
    public async Task<ChatThread?> GetThreadForFileChange(Guid id, CancellationToken cancellationToken) =>
        (await db.Threads
            .FromSql($"SELECT * FROM threads WHERE id = {id} FOR NO KEY UPDATE")
            .ToListAsync(cancellationToken))
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

    /// <summary>The caller's own unarchived threads across bots in the space, newest activity first.</summary>
    public Task<List<ChatThread>> ListOwnedThreads(Guid ownerUserId, int limit, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.OwnerUserId == ownerUserId && thread.ArchivedAt == null)
            .OrderByDescending(thread => thread.LastActivityAt)
            .ThenByDescending(thread => thread.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>The threads with one bot that the caller may see and has not archived, newest activity first.</summary>
    public Task<List<ChatThread>> ListThreadsForBot(Guid botId, int limit, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.BotId == botId && thread.ArchivedAt == null)
            .OrderByDescending(thread => thread.LastActivityAt)
            .ThenByDescending(thread => thread.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>The threads in one project that the caller may see and has not archived, newest activity first.</summary>
    public Task<List<ChatThread>> ListThreadsForProject(Guid projectId, int limit, CancellationToken cancellationToken) =>
        db.Threads.AsNoTracking()
            .Where(thread => thread.ProjectId == projectId && thread.ArchivedAt == null)
            .OrderByDescending(thread => thread.LastActivityAt)
            .ThenByDescending(thread => thread.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    #endregion

    #region Seen

    /// <summary>
    /// Marks the thread seen by the person as far as its newest message, now. A marker never moves back: two screens
    /// marking at once keep the further one. Its audience is the person, so the database lets nobody else read or
    /// write it.
    /// </summary>
    public Task MarkSeen(ChatThread thread, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO thread_reads (space_id, thread_id, audience_user_id, seen_through_position, seen_at)
            VALUES (
              sys.current_space(), {thread.Id}, {userId},
              COALESCE((SELECT max(position) FROM messages WHERE thread_id = {thread.Id} AND shelved_at IS NULL), 0),
              {now})
            ON CONFLICT (space_id, thread_id, audience_user_id) DO UPDATE
            SET seen_through_position = GREATEST(thread_reads.seen_through_position, EXCLUDED.seen_through_position),
                seen_at = GREATEST(thread_reads.seen_at, EXCLUDED.seen_at)
            """,
            cancellationToken);

    /// <summary>
    /// What each of the threads holds for the person, from their own runs and their own marker only. Runs have no
    /// audience, so another member's runs in a shared thread are visible here; they are left out by name. A run
    /// another bot handed work to is part of its root's tree: its approvals count for the root's thread, and its
    /// failure is the parent's to deal with, so it never stands as a thread's newest run.
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
                WHERE thread_id = ANY({ids}) AND principal_user_id = {userId} AND parent_run_id IS NULL
                ORDER BY thread_id, id DESC
                """)
            .AsNoTracking()
            .ToDictionaryAsync(run => run.ThreadId, cancellationToken);
        Guid[] waitingRoots = [.. newest.Values.Where(run => run.Status == RunStatus.Waiting).Select(run => run.Id)];
        var open = (await (
                from call in db.ToolCalls.AsNoTracking()
                join run in db.Runs.AsNoTracking() on call.RunId equals run.Id
                where call.Status == ToolCallStatus.PendingApproval && waitingRoots.Contains(run.RootRunId)
                select run.RootRunId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var seen = await db.ThreadReads.AsNoTracking()
            .Where(read => ids.Contains(read.ThreadId) && read.AudienceUserId == userId)
            .ToDictionaryAsync(read => read.ThreadId, read => read.SeenAt, cancellationToken);
        var withNew = (await db.Messages.AsNoTracking()
            .Where(message => ids.Contains(message.ThreadId) && message.Role == MessageRole.Assistant && message.Position != null
                && message.ShelvedAt == null)
            .Where(message => db.Runs.Any(run => run.Id == message.RunId && run.PrincipalUserId == userId && run.ParentRunId == null))
            .Where(message => message.Position > (db.ThreadReads
                .Where(read => read.ThreadId == message.ThreadId && read.AudienceUserId == userId)
                .Select(read => (int?)read.SeenThroughPosition)
                .FirstOrDefault() ?? 0))
            .Select(message => message.ThreadId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        return ids.Distinct().ToDictionary(
            id => id,
            id => new ThreadFacts(
                newest.GetValueOrDefault(id),
                newest.GetValueOrDefault(id) is { } root && open.Contains(root.Id),
                seen.TryGetValue(id, out var at) ? at : null,
                withNew.Contains(id)));
    }

    #endregion
}
