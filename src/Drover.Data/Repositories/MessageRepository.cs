namespace Drover.Data.Repositories;

/// <summary>Stores and reads the messages of a thread. Runs inside the caller's tenant scope.</summary>
public sealed class MessageRepository(DroverDbContext db)
{
    #region Public Methods

    public async Task<ChatThread?> GetThread(Guid threadId, CancellationToken cancellationToken) =>
        await db.Threads.AsNoTracking().SingleOrDefaultAsync(thread => thread.Id == threadId, cancellationToken);

    public async Task<IReadOnlyList<Message>> ListMessages(Guid threadId, CancellationToken cancellationToken) =>
        await db.Messages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId)
            .OrderBy(message => message.Position)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Adds the messages after the thread's last one, in order. A thread has one run working on it at a time, and
    /// the unique position index rejects a second writer that slips past that rule.
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
            db.Messages.Add(message);
        }
    }

    #endregion
}
