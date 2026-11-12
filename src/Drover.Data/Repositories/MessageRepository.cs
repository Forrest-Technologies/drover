namespace Drover.Data.Repositories;

/// <summary>Stores and reads the messages of a thread. Runs inside the caller's tenant scope.</summary>
public sealed class MessageRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<ChatThread?> GetThread(Guid threadId, CancellationToken cancellationToken) =>
        await db.Threads.AsNoTracking().SingleOrDefaultAsync(thread => thread.Id == threadId, cancellationToken);

    /// <summary>The conversation: every message that has a position, in order. Pending messages are not part of it yet.</summary>
    public async Task<IReadOnlyList<Message>> ListMessages(Guid threadId, CancellationToken cancellationToken) =>
        await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.Position != null)
            .OrderBy(message => message.Position)
            .ToListAsync(cancellationToken);

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
        var last = await db.Messages
            .Where(message => message.ThreadId == thread.Id)
            .MaxAsync(message => (int?)message.Position, cancellationToken) ?? 0;

        foreach (var message in messages)
        {
            message.ThreadId = thread.Id;
            message.AudienceUserId = thread.AudienceUserId;
            message.Position = ++last;
            message.Steers = false;

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

    #endregion
}
