namespace Drover.Data.Repositories;

/// <summary>An event about to be appended: which run of the tree it is about, its type, and its data.</summary>
public sealed record NewRunEvent(Guid RunId, string Type, JsonElement Data);

/// <summary>
/// The event log of a run tree. Events are numbered per root run by adding to <c>runs.last_event_seq</c> in the
/// transaction that inserts them: the row lock makes the order of numbers the order of commits, so a reader never
/// sees event 12 before event 11. Runs inside the caller's tenant scope.
/// </summary>
public sealed class RunEventRepository(DroverDbContext db)
{
    #region Public Methods

    /// <summary>
    /// Appends the events in order and returns them as stored. Pending tracked changes are saved first, so the rows
    /// the events point at exist. The time of each event is the database's clock after the lock is held, so it
    /// follows commit order too. An execution passes its <paramref name="fence"/>: if the run belongs to another
    /// claim by now, nothing is appended and <see cref="LeaseLostException"/> is thrown, so the caller's transaction
    /// rolls back with it.
    /// </summary>
    public async Task<IReadOnlyList<RunEvent>> Append(
        Guid rootRunId,
        ChatThread thread,
        IReadOnlyList<NewRunEvent> events,
        CancellationToken cancellationToken,
        RunFence? fence = null)
    {
        if (events.Count == 0)
        {
            return [];
        }

        // An execution's claim is checked before anything else of the transaction is written: the status change that
        // travels with a final event clears the lease, and must not be mistaken for a lost one. A request has no
        // claim, and its new run row must exist before its first event can count against it.
        if (fence is null)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var fenced = fence is not null;
        var last = await (fence is null
            ? db.Database.SqlQuery<long>(
                $"""
                UPDATE runs SET last_event_seq = last_event_seq + {events.Count}
                WHERE id = {rootRunId} RETURNING last_event_seq AS "Value"
                """)
            : db.Database.SqlQuery<long>(
                $"""
                UPDATE runs SET last_event_seq = last_event_seq + {events.Count}
                WHERE id = {rootRunId} AND lease_owner = {fence.Worker} AND attempt = {fence.Attempt} AND status = 'running'
                RETURNING last_event_seq AS "Value"
                """))
            .ToListAsync(cancellationToken);

        if (last.Count != 1 && fenced)
        {
            throw new LeaseLostException(rootRunId);
        }

        if (last.Count != 1)
        {
            throw new InvalidOperationException($"Root run {rootRunId} is not visible, so nothing can be appended to its events.");
        }

        await db.SaveChangesAsync(cancellationToken);
        var seq = last[0] - events.Count;
        List<RunEvent> stored = [];

        foreach (var appended in events)
        {
            stored.Add(await Insert(rootRunId, ++seq, thread, appended, cancellationToken));
        }

        return stored;
    }

    /// <summary>The events of one tree after a number, oldest first, at most <paramref name="limit"/> of them.</summary>
    public Task<List<RunEvent>> ListEvents(Guid rootRunId, long afterSeq, int limit, CancellationToken cancellationToken) =>
        db.RunEvents.AsNoTracking()
            .Where(found => found.RootRunId == rootRunId && found.Seq > afterSeq)
            .OrderBy(found => found.Seq)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The events with one of the given types that the root runs wrote themselves, not the runs they started: each
    /// tree's oldest first, the trees in the order given.
    /// </summary>
    public async Task<List<RunEvent>> ListOwnEventsOfRoots(
        IReadOnlyList<Guid> rootRunIds,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken)
    {
        var found = await db.RunEvents.AsNoTracking()
            .Where(stored => rootRunIds.Contains(stored.RootRunId) && stored.RunId == stored.RootRunId && types.Contains(stored.Type))
            .ToListAsync(cancellationToken);
        var order = rootRunIds.Select((id, index) => (id, index)).ToDictionary(root => root.id, root => root.index);
        return [.. found.OrderBy(stored => order[stored.RootRunId]).ThenBy(stored => stored.Seq)];
    }

    /// <summary>The newest event of one run with one of the given types, or null.</summary>
    public Task<RunEvent?> FindLastEvent(Guid runId, IReadOnlyCollection<string> types, CancellationToken cancellationToken) =>
        db.RunEvents.AsNoTracking()
            .Where(found => found.RunId == runId && types.Contains(found.Type))
            .OrderByDescending(found => found.Seq)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The events of one run with one of the given types, oldest first.</summary>
    public Task<List<RunEvent>> ListEventsOfRun(Guid runId, IReadOnlyCollection<string> types, CancellationToken cancellationToken) =>
        db.RunEvents.AsNoTracking()
            .Where(found => found.RunId == runId && types.Contains(found.Type))
            .OrderBy(found => found.Seq)
            .ToListAsync(cancellationToken);

    #endregion

    #region Private Methods

    private async Task<RunEvent> Insert(
        Guid rootRunId,
        long seq,
        ChatThread thread,
        NewRunEvent appended,
        CancellationToken cancellationToken)
    {
        var data = appended.Data.GetRawText();

        // The data comes back as the database stored it: jsonb orders keys its own way, and what is published live
        // must be byte for byte what a replay reads.
        var inserted = await db.Database
            .SqlQuery<InsertedEvent>(
                $"""
                INSERT INTO run_events (space_id, root_run_id, seq, created_at, run_id, thread_id, audience_user_id, type, data)
                VALUES (sys.current_space(), {rootRunId}, {seq}, clock_timestamp(), {appended.RunId}, {thread.Id},
                        {thread.AudienceUserId}, {appended.Type}, {data}::jsonb)
                RETURNING created_at, data::text AS stored
                """)
            .ToListAsync(cancellationToken);

        return new RunEvent
        {
            SpaceId = thread.SpaceId,
            RootRunId = rootRunId,
            Seq = seq,
            CreatedAt = inserted[0].CreatedAt,
            RunId = appended.RunId,
            ThreadId = thread.Id,
            AudienceUserId = thread.AudienceUserId,
            Type = appended.Type,
            Data = JsonElement.Parse(inserted[0].Stored),
        };
    }

    #endregion

    #region Nested Types

    private sealed record InsertedEvent(DateTimeOffset CreatedAt, string Stored);

    #endregion
}
