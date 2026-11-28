namespace Drover.Data.Repositories;

/// <summary>Stores and reads the messages of a thread. Runs inside the caller's tenant scope.</summary>
public sealed class MessageRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<ChatThread?> GetThread(Guid threadId, CancellationToken cancellationToken) =>
        await db.Threads.AsNoTracking().SingleOrDefaultAsync(thread => thread.Id == threadId, cancellationToken);

    /// <summary>
    /// The conversation: every message that has a position and is in the version shown, in order. Pending messages
    /// are not part of it yet, and the messages of a version a person moved away from are not part of it now.
    /// </summary>
    public async Task<IReadOnlyList<Message>> ListMessages(Guid threadId, CancellationToken cancellationToken) =>
        await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Position != null && message.ShelvedAt == null)
            .OrderBy(message => message.Position)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The last <paramref name="count"/> messages of the conversation that is shown, oldest first.
    /// When requested, only include user and assistant messages that a model may read.
    /// </summary>
    public async Task<IReadOnlyList<Message>> ListLatestMessages(
        Guid threadId,
        int count,
        CancellationToken cancellationToken,
        bool conversationOnly = false)
    {
        var messages = db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Position != null && message.ShelvedAt == null);

        if (conversationOnly)
        {
            messages = messages.Where(message =>
                !message.Withheld && (message.Role == MessageRole.User || message.Role == MessageRole.Assistant));
        }

        var latest = await messages
            .OrderByDescending(message => message.Position)
            .Take(count)
            .ToListAsync(cancellationToken);
        latest.Reverse();
        return latest;
    }

    /// <summary>The latest bot reply in the conversation version the person sees.</summary>
    public Task<Message?> FindLatestReply(Guid threadId, CancellationToken cancellationToken) =>
        db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Role == MessageRole.Assistant
                && message.Position != null && message.ShelvedAt == null && !message.Withheld)
            .OrderByDescending(message => message.Position)
            .ThenByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The bot's final reply stored for <paramref name="runId"/> in the shown conversation.</summary>
    public Task<Message?> FindFinalReplyForRun(Guid threadId, Guid runId, CancellationToken cancellationToken) =>
        db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.RunId == runId
                && message.Role == MessageRole.Assistant
                && message.Position != null && message.ShelvedAt == null && !message.Withheld)
            .OrderByDescending(message => message.Position)
            .ThenByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The latest non-withheld message from a person in the shown conversation.</summary>
    public Task<Message?> FindLatestPersonMessage(Guid threadId, CancellationToken cancellationToken) =>
        db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Role == MessageRole.User
                && message.ScheduleId == null
                && message.Position != null && message.ShelvedAt == null && !message.Withheld)
            .OrderByDescending(message => message.Position)
            .ThenByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The versions of each place in the conversation that has more than one, by the id of every version: the ids of
    /// the messages that share its parent, oldest first. A place with one version is left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListVersions(Guid threadId, CancellationToken cancellationToken)
    {
        var placed = await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Position != null)
            .Select(message => new { message.Id, message.ParentId, message.CreatedAt })
            .ToListAsync(cancellationToken);
        Dictionary<Guid, IReadOnlyList<Guid>> versions = [];

        foreach (var place in placed.GroupBy(message => message.ParentId).Where(place => place.Count() > 1))
        {
            List<Guid> ids = [.. place.OrderBy(message => message.CreatedAt).ThenBy(message => message.Id).Select(message => message.Id)];

            foreach (var id in ids)
            {
                versions[id] = ids;
            }
        }

        return versions;
    }

    /// <summary>
    /// Takes every message at <paramref name="position"/> and after it out of the version shown. They are kept, and
    /// come back when their version is chosen again. Pending messages have no position and stay pending.
    /// </summary>
    public Task Shelve(Guid threadId, int position, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Messages
            .Where(message => message.ThreadId == threadId && message.ShelvedAt == null && message.Position >= position)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.ShelvedAt, now), cancellationToken);

    /// <summary>
    /// Brings a version back into the conversation shown, with what followed it: from each message, the message that
    /// was shown after it when its version was left, which is the one shelved last. The place must be free: the
    /// caller shelves the version shown there first.
    /// </summary>
    public async Task Restore(Guid threadId, Guid messageId, CancellationToken cancellationToken)
    {
        var shelved = await db.Messages
            .Where(message => message.ThreadId == threadId && message.ShelvedAt != null && message.Position != null)
            .ToListAsync(cancellationToken);
        var next = shelved.SingleOrDefault(message => message.Id == messageId);

        while (next is not null)
        {
            var parent = next;
            parent.ShelvedAt = null;
            next = shelved
                .Where(message => message.ParentId == parent.Id && message.ShelvedAt != null)
                .OrderByDescending(message => message.ShelvedAt)
                .ThenByDescending(message => message.Id)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Stores <paramref name="message"/> as another version of <paramref name="of"/>: in its place, after the same
    /// message. The caller shelves <paramref name="of"/> and what follows it first.
    /// </summary>
    public void AddVersion(ChatThread thread, Message of, Message message)
    {
        message.ThreadId = thread.Id;
        message.AudienceUserId = thread.AudienceUserId;
        message.Position = of.Position;
        message.ParentId = of.ParentId;
        message.Steers = false;
        db.Messages.Add(message);
    }

    /// <summary>
    /// The messages sent while the bot was working that no run has taken yet, in the order they were sent. Tracked,
    /// so the caller can place them with <see cref="AppendMessages"/>.
    /// </summary>
    public async Task<IReadOnlyList<Message>> ListPendingMessages(Guid threadId, CancellationToken cancellationToken) =>
        await db.Messages
            .Where(message => message.ThreadId == threadId && message.Position == null)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The messages a run took after they had waited as pending: a person's messages that carry the run's id and
    /// have a position, in order.
    /// </summary>
    public async Task<IReadOnlyList<Message>> ListDeliveredMessages(Guid threadId, Guid runId, CancellationToken cancellationToken) =>
        await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.RunId == runId
                && message.Role == MessageRole.User && message.Position != null)
            .OrderBy(message => message.Position)
            .ToListAsync(cancellationToken);

    /// <summary>The tool calls of a thread that have a result in the conversation.</summary>
    public async Task<HashSet<Guid>> ListAnsweredCalls(Guid threadId, CancellationToken cancellationToken) =>
    [
        .. (await db.Messages
                .AsNoTracking()
                .Where(message => message.ThreadId == threadId && message.Role == MessageRole.Tool && message.Position != null)
                .Select(message => message.Parts)
                .ToListAsync(cancellationToken))
            .SelectMany(parts => parts.OfType<ToolResultPart>())
            .Select(result => result.ToolCallId),
    ];

    /// <summary>Marks a message as kept from the bot by a rule. No model is given a message marked so.</summary>
    public Task MarkWithheld(Guid id, CancellationToken cancellationToken) =>
        db.Messages.Where(message => message.Id == id).ExecuteUpdateAsync(set => set.SetProperty(message => message.Withheld, true), cancellationToken);

    /// <summary>
    /// The run's reply in the thread that asked for the tool call, or null when it is not the run's latest stored reply.
    /// A tool runs before the next model call is stored, so the reply that asked for it is the latest one; only that
    /// one row is read.
    /// </summary>
    public Task<Message?> FindReplyWithCall(Guid threadId, Guid runId, Guid toolCallId, CancellationToken cancellationToken) =>
        FindReply(db.Messages.AsNoTracking(), threadId, runId, toolCallId, cancellationToken);

    /// <summary>
    /// As <see cref="FindReplyWithCall"/>, locked until the scope ends and tracked, so a part added to it is saved
    /// when the scope commits. Its parts are read under the lock, so two calls that add to one reply at once each see
    /// the other's part.
    /// </summary>
    public async Task<Message?> GetReplyWithCallForUpdate(Guid threadId, Guid runId, Guid toolCallId, CancellationToken cancellationToken) =>
        await FindReply(db.Messages.AsNoTracking(), threadId, runId, toolCallId, cancellationToken) is { } reply
            ? (await db.Messages.FromSql($"SELECT * FROM messages WHERE id = {reply.Id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
            : null;

    public Task<Message?> FindMessage(Guid id, CancellationToken cancellationToken) =>
        db.Messages.AsNoTracking().SingleOrDefaultAsync(message => message.Id == id, cancellationToken);

    /// <summary>
    /// Puts the messages after the thread's last one, in order: new ones are added, pending ones get their position.
    /// A thread has one run working on it at a time, and the unique position index rejects a second writer that
    /// slips past that rule.
    /// </summary>
    public async Task AppendMessages(
        ChatThread thread,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken)
    {
        var (last, parentId) = await FindLastShown(thread.Id, cancellationToken);

        foreach (var message in messages)
        {
            message.ThreadId = thread.Id;
            message.AudienceUserId = thread.AudienceUserId;
            message.Position = ++last;
            message.ParentId = parentId;
            message.Steers = false;
            parentId = message.Id;

            if (db.Entry(message).State is EntityState.Detached)
            {
                db.Messages.Add(message);
            }
        }
    }

    /// <summary>
    /// The messages of the conversations the caller may read whose words match <paramref name="words"/>, best match
    /// first, at most <paramref name="limit"/> within a window of <paramref name="candidateLimit"/> placed messages: the
    /// newest ones, or the newest sent before <paramref name="before"/>. Only what a person or a bot said is searched:
    /// the text parts of placed messages in threads that are not archived, of bots that are not removed (a hit in a
    /// conversation with a removed bot could not be opened). Row-level security keeps this to the caller's space and to
    /// the threads whose audience includes them: their own, and the ones shared with everyone. With
    /// <paramref name="mineOnly"/> an owner test narrows it further to the threads that belong to the caller; the
    /// policies apply in both modes, and the owner test only ever narrows. A partial descending index over the space's
    /// recent placed messages bounds the window so text extraction, tokenization, and ranking never scan unbounded
    /// history, and a statement timeout caps execution time. The hits and the place where the window ended come from one
    /// statement, so they describe the same set of messages even while threads are archived or messages shelved: the
    /// next page starts at the last message this one looked at. Each snippet marks a matched word with
    /// <see cref="MatchStart"/> and <see cref="MatchEnd"/>, which are taken out of the text before it is marked. Call it
    /// inside a tenant transaction: it throws without one, because the timeout is set for the transaction.
    /// </summary>
    public async Task<MessageSearch> SearchMessages(
        string words,
        int limit,
        MessageKey? before,
        bool mineOnly,
        CancellationToken cancellationToken,
        int candidateLimit = SearchCandidateLimit)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A conversation search runs inside a tenant transaction so its time limit applies.");
        }

        await db.Database.ExecuteSqlRawAsync(SearchStatementTimeoutSql, cancellationToken);

        // Without a cursor the window starts at the newest message: nothing is sent after the end of time.
        var after = before ?? new MessageKey(DateTimeOffset.MaxValue, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var extra = candidateLimit + 1;

        // One more message than the window is read, only to know whether older messages remain. The window is the
        // first candidateLimit of them, and the message that ends it is where the next page starts.
        var rows = await db.Database
            .SqlQuery<MessageSearchRow>(
                $"""
                WITH ranked AS MATERIALIZED (
                    SELECT pool.*, row_number() OVER (ORDER BY pool.created_at DESC, pool.id DESC) AS rn
                    FROM (
                        SELECT m.id, m.space_id, m.thread_id, m.role, m.created_at, m.parts,
                               t.bot_id, b.name AS bot_name, t.title AS thread_title,
                               (t.is_main AND t.owner_user_id = sys.current_user_id()) AS in_main_conversation
                        FROM messages m
                        JOIN threads t ON t.space_id = m.space_id AND t.id = m.thread_id
                        JOIN bots b ON b.space_id = t.space_id AND b.id = t.bot_id
                        WHERE m.position IS NOT NULL
                          AND m.shelved_at IS NULL
                          AND m.role IN ('user', 'assistant')
                          AND t.archived_at IS NULL
                          AND b.status <> 'removed'
                          AND (NOT {mineOnly} OR t.owner_user_id = sys.current_user_id())
                          AND (m.created_at, m.id) < ({after.SentAt}, {after.Id})
                        ORDER BY m.created_at DESC, m.id DESC
                        LIMIT {extra}
                    ) AS pool
                ),
                found AS (
                    SELECT c.id AS message_id, c.thread_id, c.bot_id, c.bot_name, c.thread_title,
                           c.in_main_conversation,
                           c.role = 'assistant' AS from_bot, c.created_at,
                           ts_headline('english', said.text, q.query, said.marks) AS snippet,
                           row_number() OVER (ORDER BY ts_rank(doc.document, q.query) DESC, c.created_at DESC, c.id DESC) AS place
                    FROM ranked c
                    CROSS JOIN websearch_to_tsquery('english', {words}) AS q(query)
                    CROSS JOIN LATERAL (
                        SELECT string_agg(translate(part.text, E'\u0001\u0002', ''), E'\n') AS text,
                               'StartSel=' || E'\u0001' || ', StopSel=' || E'\u0002' || ', MaxFragments=1, MaxWords=24, MinWords=10' AS marks
                        FROM jsonb_array_elements_text(jsonb_path_query_array(c.parts, '$[*] ? (@.type == "text").text')) AS part(text)
                    ) AS said
                    CROSS JOIN LATERAL (
                        SELECT to_tsvector('english', said.text) AS document
                    ) AS doc
                    WHERE c.rn <= {candidateLimit}
                      AND said.text IS NOT NULL
                      AND doc.document @@ q.query
                    ORDER BY place
                    LIMIT {limit}
                ),
                edge AS (
                    SELECT last.created_at AS edge_sent_at, last.id AS edge_id
                    FROM ranked last
                    WHERE last.rn = {candidateLimit}
                      AND EXISTS (SELECT 1 FROM ranked next WHERE next.rn = {extra})
                )
                SELECT f.message_id, f.thread_id, f.bot_id, f.bot_name, f.thread_title, f.in_main_conversation,
                       f.from_bot, f.created_at, f.snippet, NULL::timestamptz AS edge_sent_at, NULL::uuid AS edge_id, f.place::bigint AS place
                FROM found f
                UNION ALL
                SELECT NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, e.edge_sent_at, e.edge_id, 9223372036854775807
                FROM edge e
                ORDER BY place
                """)
            .ToListAsync(cancellationToken);

        var older = rows.FirstOrDefault(row => row.MessageId is null) is { EdgeId: { } id, EdgeSentAt: { } sentAt }
            ? new MessageKey(sentAt, id)
            : null;

        return new([.. rows.Where(row => row.MessageId is not null).Select(row => row.ToMatch())], older);
    }

    /// <summary>Stores a message without a position: it waits for a run to take it.</summary>
    public void AddPending(ChatThread thread, Message message)
    {
        message.ThreadId = thread.Id;
        message.AudienceUserId = thread.AudienceUserId;
        message.Position = null;
        db.Messages.Add(message);
    }

    /// <summary>
    /// One page of placed conversation messages for export: only shown roles and only the part fields an export keeps,
    /// projected in SQL so reasoning and tool arguments/results never cross the wire.
    /// </summary>
    public Task<List<ExportMessageRow>> ListExportPlacedMessages(
        Guid threadId,
        int? afterPosition,
        int limit,
        CancellationToken cancellationToken) =>
        afterPosition is { } cursor
            ? db.Database.SqlQuery<ExportMessageRow>($"""
                SELECT id,
                       position,
                       role,
                       created_at,
                       withheld,
                       schedule_id,
                       (SELECT COALESCE(jsonb_agg(
                           CASE item.p->>'type'
                             WHEN 'text' THEN jsonb_build_object('type', 'text', 'text', item.p->'text')
                             WHEN 'image' THEN jsonb_build_object('type', 'image', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'file' THEN jsonb_build_object('type', 'file', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'tool_call' THEN jsonb_build_object('type', 'tool_call', 'tool_call_id', item.p->'tool_call_id', 'tool_name', item.p->'tool_name')
                           END
                           ORDER BY item.ord), '[]'::jsonb)
                        FROM jsonb_array_elements(parts) WITH ORDINALITY AS item(p, ord)
                        WHERE item.p->>'type' IN ('text', 'image', 'file', 'tool_call'))::text AS parts_json
                FROM messages
                WHERE thread_id = {threadId}
                  AND position IS NOT NULL
                  AND shelved_at IS NULL
                  AND role IN ('user', 'assistant')
                  AND position > {cursor}
                ORDER BY position
                LIMIT {limit}
                """).ToListAsync(cancellationToken)
            : db.Database.SqlQuery<ExportMessageRow>($"""
                SELECT id,
                       position,
                       role,
                       created_at,
                       withheld,
                       schedule_id,
                       (SELECT COALESCE(jsonb_agg(
                           CASE item.p->>'type'
                             WHEN 'text' THEN jsonb_build_object('type', 'text', 'text', item.p->'text')
                             WHEN 'image' THEN jsonb_build_object('type', 'image', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'file' THEN jsonb_build_object('type', 'file', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'tool_call' THEN jsonb_build_object('type', 'tool_call', 'tool_call_id', item.p->'tool_call_id', 'tool_name', item.p->'tool_name')
                           END
                           ORDER BY item.ord), '[]'::jsonb)
                        FROM jsonb_array_elements(parts) WITH ORDINALITY AS item(p, ord)
                        WHERE item.p->>'type' IN ('text', 'image', 'file', 'tool_call'))::text AS parts_json
                FROM messages
                WHERE thread_id = {threadId}
                  AND position IS NOT NULL
                  AND shelved_at IS NULL
                  AND role IN ('user', 'assistant')
                ORDER BY position
                LIMIT {limit}
                """).ToListAsync(cancellationToken);

    /// <summary>One page of pending conversation messages for export, using the same slim SQL projection.</summary>
    public Task<List<ExportMessageRow>> ListExportPendingMessages(
        Guid threadId,
        DateTimeOffset? afterCreatedAt,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken) =>
        afterCreatedAt is { } createdCursor && afterId is { } idCursor
            ? db.Database.SqlQuery<ExportMessageRow>($"""
                SELECT id,
                       position,
                       role,
                       created_at,
                       withheld,
                       schedule_id,
                       (SELECT COALESCE(jsonb_agg(
                           CASE item.p->>'type'
                             WHEN 'text' THEN jsonb_build_object('type', 'text', 'text', item.p->'text')
                             WHEN 'image' THEN jsonb_build_object('type', 'image', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'file' THEN jsonb_build_object('type', 'file', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'tool_call' THEN jsonb_build_object('type', 'tool_call', 'tool_call_id', item.p->'tool_call_id', 'tool_name', item.p->'tool_name')
                           END
                           ORDER BY item.ord), '[]'::jsonb)
                        FROM jsonb_array_elements(parts) WITH ORDINALITY AS item(p, ord)
                        WHERE item.p->>'type' IN ('text', 'image', 'file', 'tool_call'))::text AS parts_json
                FROM messages
                WHERE thread_id = {threadId}
                  AND position IS NULL
                  AND role IN ('user', 'assistant')
                  AND (created_at, id) > ({createdCursor}, {idCursor})
                ORDER BY created_at, id
                LIMIT {limit}
                """).ToListAsync(cancellationToken)
            : db.Database.SqlQuery<ExportMessageRow>($"""
                SELECT id,
                       position,
                       role,
                       created_at,
                       withheld,
                       schedule_id,
                       (SELECT COALESCE(jsonb_agg(
                           CASE item.p->>'type'
                             WHEN 'text' THEN jsonb_build_object('type', 'text', 'text', item.p->'text')
                             WHEN 'image' THEN jsonb_build_object('type', 'image', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'file' THEN jsonb_build_object('type', 'file', 'name', item.p->'name', 'media_type', item.p->'media_type', 'size', item.p->'size')
                             WHEN 'tool_call' THEN jsonb_build_object('type', 'tool_call', 'tool_call_id', item.p->'tool_call_id', 'tool_name', item.p->'tool_name')
                           END
                           ORDER BY item.ord), '[]'::jsonb)
                        FROM jsonb_array_elements(parts) WITH ORDINALITY AS item(p, ord)
                        WHERE item.p->>'type' IN ('text', 'image', 'file', 'tool_call'))::text AS parts_json
                FROM messages
                WHERE thread_id = {threadId}
                  AND position IS NULL
                  AND role IN ('user', 'assistant')
                ORDER BY created_at, id
                LIMIT {limit}
                """).ToListAsync(cancellationToken);

    /// <summary>Fallback outcome lookup from stored tool result parts when a tool call row is missing.</summary>
    public async Task<Dictionary<Guid, bool>> ListToolResultErrors(
        Guid threadId,
        IReadOnlyCollection<Guid> toolCallIds,
        CancellationToken cancellationToken)
    {
        if (toolCallIds.Count == 0)
        {
            return [];
        }

        var idArray = toolCallIds.ToArray();
        var rows = await db.Database.SqlQuery<ExportToolResultRow>($"""
            SELECT (item.p->>'tool_call_id')::uuid AS tool_call_id,
                   COALESCE((item.p->>'is_error')::boolean, false) AS is_error
            FROM messages,
                 LATERAL jsonb_array_elements(parts) AS item(p)
            WHERE thread_id = {threadId}
              AND position IS NOT NULL
              AND shelved_at IS NULL
              AND role = 'tool'
              AND item.p->>'type' = 'tool_result'
              AND (item.p->>'tool_call_id')::uuid = ANY ({idArray})
            ORDER BY position
            """).ToListAsync(cancellationToken);

        Dictionary<Guid, bool> results = [];

        foreach (var row in rows)
        {
            results[row.ToolCallId] = row.IsError;
        }

        return results;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The position and id of the last message of the version shown, counting messages placed in this unit of work
    /// and not yet saved; (0, null) for an empty conversation.
    /// </summary>
    private async Task<(int Position, Guid? Id)> FindLastShown(Guid threadId, CancellationToken cancellationToken)
    {
        var stored = await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Position != null && message.ShelvedAt == null)
            .OrderByDescending(message => message.Position)
            .Select(message => new { message.Id, message.Position })
            .FirstOrDefaultAsync(cancellationToken);
        var placed = db.ChangeTracker.Entries<Message>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)
            .Where(message => message.ThreadId == threadId && message.Position is not null && message.ShelvedAt is null)
            .MaxBy(message => message.Position);

        return placed is not null && placed.Position > (stored?.Position ?? 0)
            ? (placed.Position!.Value, placed.Id)
            : (stored?.Position ?? 0, stored?.Id);
    }

    private static async Task<Message?> FindReply(
        IQueryable<Message> messages,
        Guid threadId,
        Guid runId,
        Guid toolCallId,
        CancellationToken cancellationToken) =>
        await messages
            .Where(message => message.ThreadId == threadId && message.RunId == runId && message.Role == MessageRole.Assistant && message.Position != null)
            .OrderByDescending(message => message.Position)
            .FirstOrDefaultAsync(cancellationToken) is { } latest
            && latest.Parts.OfType<ToolCallPart>().Any(call => call.ToolCallId == toolCallId)
            ? latest
            : null;

    #endregion

    #region Search Bounds And Marks

    /// <summary>The most recent readable placed messages scanned per search so growing history stays bounded.</summary>
    public const int SearchCandidateLimit = 2000;

    /// <summary>The longest a search statement may run on the database, in milliseconds.</summary>
    public const int SearchTimeoutMilliseconds = 5000;

    private const string SearchStatementTimeoutSql = "SET LOCAL statement_timeout = '5000'";

    /// <summary>Marks where a matched word starts in a search snippet.</summary>
    public const char MatchStart = '\u0001';

    /// <summary>Marks where a matched word ends in a search snippet.</summary>
    public const char MatchEnd = '\u0002';

    #endregion
}


/// <summary>
/// A message that matched a search, with what is needed to show it and to open its conversation at it: the bot and
/// the thread it was said in, whether that is the caller's main conversation with the bot, who said it, when, and a
/// snippet of its words around the match.
/// </summary>
public sealed record MessageMatch(
    Guid MessageId,
    Guid ThreadId,
    Guid BotId,
    string BotName,
    string? ThreadTitle,
    bool InMainConversation,
    bool FromBot,
    DateTimeOffset CreatedAt,
    string Snippet);


/// <summary>A place in a space's messages, newest first: when a message was sent, and which message it was.</summary>
public sealed record MessageKey(DateTimeOffset SentAt, Guid Id);


/// <summary>
/// The messages a search found, and where its window ended: <paramref name="Older"/> is the last message the search
/// looked at when older readable messages remain, and null when it looked at all of them.
/// </summary>
public sealed record MessageSearch(IReadOnlyList<MessageMatch> Matches, MessageKey? Older);


/// <summary>
/// One row of the search statement: a message that matched, or the single row that says where the window ended.
/// Columns that do not belong to the kind of row are null.
/// </summary>
internal sealed record MessageSearchRow(
    Guid? MessageId,
    Guid? ThreadId,
    Guid? BotId,
    string? BotName,
    string? ThreadTitle,
    bool? InMainConversation,
    bool? FromBot,
    DateTimeOffset? CreatedAt,
    string? Snippet,
    DateTimeOffset? EdgeSentAt,
    Guid? EdgeId,
    long Place)
{
    public MessageMatch ToMatch() => new(
        MessageId!.Value,
        ThreadId!.Value,
        BotId!.Value,
        BotName!,
        ThreadTitle,
        InMainConversation!.Value,
        FromBot!.Value,
        CreatedAt!.Value,
        Snippet!);
}


/// <summary>One message as projected for conversation export, holding only the parts an export shows.</summary>
public sealed record ExportMessageRow(
    Guid Id,
    int? Position,
    string Role,
    DateTimeOffset CreatedAt,
    bool Withheld,
    Guid? ScheduleId,
    string PartsJson);

internal sealed record ExportToolResultRow(Guid ToolCallId, bool IsError);
