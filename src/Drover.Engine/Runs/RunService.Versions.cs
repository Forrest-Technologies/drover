namespace Drover.Engine.Runs;

public enum VersionOutcome
{
    Queued,
    Shown,
    NotFound,
    Forbidden,
    Busy,
    Invalid,
    BotUnavailable,
}

/// <summary>What asking for another version of the conversation came to: the new message and run, or why nothing changed.</summary>
public sealed record VersionChange(VersionOutcome Outcome, Message? Message = null, Run? Run = null, string? Detail = null);

/// <summary>
/// Versions of a conversation. A person may edit a message of theirs or ask for another reply, and switch between
/// the versions that made. Nothing is deleted: a version left behind is shelved, with its runs, events, and usage as
/// they were, and comes back when it is chosen again. The model reads the version shown and nothing else.
/// </summary>
public sealed partial class RunService
{
    #region Private Fields

    private const string NothingAnswered = "This reply answers no message of a person, so there is nothing to answer again.";

    #endregion

    #region Public Methods

    /// <summary>
    /// Answers again the person's message a reply answers: the last message of a person before it. Everything after
    /// that message is shelved and a new run answers it, with the conversation's current model. Only the person who
    /// wrote the message may, and only while nothing works on the thread.
    /// </summary>
    public async Task<VersionChange> Regenerate(Guid threadId, Guid replyId, Guid userId, CancellationToken cancellationToken)
    {
        QueuedRun queued;
        Message asked;

        await using (var work = await scope.Begin(cancellationToken))
        {
            var thread = await threads.GetThreadForUpdate(threadId, cancellationToken);
            var reply = thread is null ? null : await messages.FindMessage(replyId, cancellationToken);

            if (thread is null || reply is not { Role: MessageRole.Assistant, Position: { } position, ShelvedAt: null } || reply.ThreadId != thread.Id)
            {
                return new VersionChange(VersionOutcome.NotFound);
            }

            var found = (await messages.ListMessages(thread.Id, cancellationToken))
                .LastOrDefault(message => message.Position < position && message.Role is MessageRole.User);

            if (found is null)
            {
                return new VersionChange(VersionOutcome.Invalid, Detail: NothingAnswered);
            }

            if (await FindVersionProblem(thread, found, userId, cancellationToken) is { } problem)
            {
                return problem;
            }

            asked = found;
            await LeaveVersion(thread, asked.Position!.Value + 1, cancellationToken);
            queued = await queue.Queue(thread, asked, userId, cancellationToken);
            await work.Commit(cancellationToken);
        }

        Started(queued);
        logger.LogInformation("Message {MessageId} in thread {ThreadId} is answered again by run {RunId}", asked.Id, threadId, queued.Run.Id);
        return new VersionChange(VersionOutcome.Queued, asked, queued.Run);
    }

    /// <summary>
    /// Stores an edited version of a person's message in its place and answers it with a new run. The message and
    /// everything after it are shelved. The edit replaces the words; files sent with the message go with it. Only the
    /// message's author may, and only while nothing works on the thread.
    /// </summary>
    public async Task<VersionChange> Edit(Guid threadId, Guid messageId, string text, Guid userId, CancellationToken cancellationToken)
    {
        QueuedRun queued;
        Message edited;

        if (text.Length > MaxMessageLength)
        {
            return new VersionChange(VersionOutcome.Invalid, Detail: $"Write a message of up to {MaxMessageLength} characters.");
        }

        await using (var work = await scope.Begin(cancellationToken))
        {
            var thread = await threads.GetThreadForUpdate(threadId, cancellationToken);
            var original = thread is null ? null : await messages.FindMessage(messageId, cancellationToken);

            if (thread is null || original is not { Role: MessageRole.User, Position: { } position, ShelvedAt: null } || original.ThreadId != thread.Id)
            {
                return new VersionChange(VersionOutcome.NotFound);
            }

            List<MessagePart> kept = [.. original.Parts.Where(part => part is not TextPart)];

            if (string.IsNullOrWhiteSpace(text) && kept.Count == 0)
            {
                return new VersionChange(VersionOutcome.Invalid, Detail: $"Write a message of up to {MaxMessageLength} characters.");
            }

            if (await FindVersionProblem(thread, original, userId, cancellationToken) is { } problem)
            {
                return problem;
            }

            edited = new Message
            {
                Role = MessageRole.User,
                AuthorUserId = userId,
                Parts = [.. string.IsNullOrWhiteSpace(text) ? Array.Empty<MessagePart>() : [new TextPart(text)], .. kept],
                CreatedAt = time.GetUtcNow(),
            };
            await LeaveVersion(thread, position, cancellationToken);
            messages.AddVersion(thread, original, edited);
            queued = await queue.Queue(thread, edited, userId, cancellationToken);
            await work.Commit(cancellationToken);
        }

        Started(queued);
        logger.LogInformation("Message {MessageId} in thread {ThreadId} was edited as {EditedId}; run {RunId} answers it", messageId, threadId, edited.Id, queued.Run.Id);
        return new VersionChange(VersionOutcome.Queued, edited, queued.Run);
    }

    /// <summary>
    /// Shows another version of a place in the conversation, with what followed it when it was left. Anyone who can
    /// read the conversation may, since nothing is lost, and only while nothing works on the thread: the next run
    /// reads the version shown.
    /// </summary>
    public async Task<VersionChange> ShowVersion(Guid threadId, Guid messageId, Guid userId, CancellationToken cancellationToken)
    {
        await using (var work = await scope.Begin(cancellationToken))
        {
            var thread = await threads.GetThreadForUpdate(threadId, cancellationToken);
            var version = thread is null ? null : await messages.FindMessage(messageId, cancellationToken);

            if (thread is null || version is not { Position: { } position } || version.ThreadId != thread.Id)
            {
                return new VersionChange(VersionOutcome.NotFound);
            }

            if (version.ShelvedAt is null)
            {
                return new VersionChange(VersionOutcome.Shown, version);
            }

            var parent = version.ParentId is { } parentId ? await messages.FindMessage(parentId, cancellationToken) : null;

            if (parent is { ShelvedAt: not null })
            {
                return new VersionChange(VersionOutcome.Invalid, Detail: "This version follows a message that is not shown. Show that message first.");
            }

            if (await runs.FindActiveRun(thread.Id, cancellationToken) is not null)
            {
                return new VersionChange(VersionOutcome.Busy);
            }

            await LeaveVersion(thread, position, cancellationToken);
            await messages.Restore(thread.Id, version.Id, cancellationToken);
            await work.Commit(cancellationToken);
        }

        if (tenant.SpaceId is { } spaceId)
        {
            signals.Publish(spaceId, userId, new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));
        }

        logger.LogInformation("Thread {ThreadId} now shows message {MessageId}", threadId, messageId);
        return new VersionChange(VersionOutcome.Shown);
    }

    #endregion

    #region Versions

    /// <summary>Why the person may not start a new version from the message, or null when they may.</summary>
    private async Task<VersionChange?> FindVersionProblem(ChatThread thread, Message message, Guid userId, CancellationToken cancellationToken)
    {
        if (message.AuthorUserId != userId)
        {
            return new VersionChange(VersionOutcome.Forbidden);
        }

        if (await runs.FindActiveRun(thread.Id, cancellationToken) is not null)
        {
            return new VersionChange(VersionOutcome.Busy);
        }

        return await bots.FindBot(thread.BotId, cancellationToken) is not { Status: BotStatus.Active }
            ? new VersionChange(VersionOutcome.BotUnavailable)
            : null;
    }

    /// <summary>
    /// Shelves the version shown from <paramref name="position"/> on. A summary that covers any of it no longer
    /// describes the conversation and is dropped, to be written again. The framework's session is working state of
    /// the version left, so it is discarded and the next run starts from the messages shown.
    /// </summary>
    private async Task LeaveVersion(ChatThread thread, int position, CancellationToken cancellationToken)
    {
        await messages.Shelve(thread.Id, position, time.GetUtcNow(), cancellationToken);
        await sessions.DeleteSession(thread.Id, thread.BotId, cancellationToken);
        thread.LastActivityAt = time.GetUtcNow();

        if (thread.SummaryThroughPosition >= position)
        {
            thread.Summary = null;
            thread.SummaryThroughPosition = null;
            thread.SummaryUpdatedAt = null;
        }
    }

    /// <summary>Tells the workers and the person's screens about a run a new version started.</summary>
    private void Started(QueuedRun queued)
    {
        hub.Publish(queued.Run.RootRunId, queued.Events);
        signal.Wake();
        Tell(queued.Run, UserSignalKinds.ThreadChanged);
    }

    #endregion
}
