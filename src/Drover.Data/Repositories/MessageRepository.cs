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
