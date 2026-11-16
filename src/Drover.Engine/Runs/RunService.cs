namespace Drover.Engine.Runs;

public enum PostProblem
{
    None,
    NotFound,
    Invalid,
}

/// <summary>What posting a message came to: the stored message and its run, or why nothing was stored.</summary>
public sealed record MessagePost(Message? Message, Run? Run, PostProblem Problem = PostProblem.None, string? Detail = null);

public enum CancelOutcome
{
    Requested,
    NotFound,
    Forbidden,
}

public sealed record RunCancel(CancelOutcome Outcome, Run? Run = null);

/// <summary>
/// Starting and stopping runs. Posting a message and cancelling open their own tenant scope, because both tell the
/// workers after the commit; their endpoints sit outside the scope filter. Reading a run happens in the scope the
/// filter opened.
/// </summary>
public sealed class RunService(
    ThreadRepository threads,
    MessageRepository messages,
    BotRepository bots,
    FileRepository files,
    FileAbilityReader abilities,
    IOptions<DeploymentPolicyOptions> deployment,
    RunRepository runs,
    RunEventRepository events,
    ToolCallRepository toolCalls,
    ThreadLine queue,
    TenantScope scope,
    RunSignal signal,
    RunEventHub hub,
    UserSignalHub signals,
    TenantContext tenant,
    TimeProvider time,
    ILogger<RunService> logger)
{
    #region Private Fields

    public const int MaxMessageLength = 100_000;

    private const int TitleLength = 80;

    private const string CancelledBeforeApproval = "The run was cancelled before this call was approved.";

    #endregion

    #region Public Methods

    /// <summary>
    /// A run, for someone who may know about it: a person who can see its thread, or a space admin, who sees
    /// activity and cost without being able to read a private thread. For anyone else it is not found, so a run id
    /// tells a member nothing about another member's private thread.
    /// </summary>
    public async Task<Run?> FindRun(Guid runId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        var run = await runs.FindRun(runId, cancellationToken);
        return run is not null && (isSpaceAdmin || await threads.FindThread(run.ThreadId, cancellationToken) is not null)
            ? run
            : null;
    }

    /// <summary>
    /// Stores the message and makes sure a run answers it, in one transaction; then wakes the workers. A thread runs
    /// one root run at a time. With none active, the message joins the conversation and a run is queued for it. With
    /// one active, the message is stored as pending and is never refused: it steers that run at its next step when
    /// <paramref name="steer"/> is set and the run acts for the sender, and otherwise waits for the run to end and
    /// starts the next one. A run only ever takes instructions from the person it acts for. Files go with the message
    /// only when the sender uploaded them to this thread, has not sent them yet, and the bot can open each one.
    /// </summary>
    public async Task<MessagePost> PostMessage(
        Guid threadId,
        string text,
        IReadOnlyList<Guid> fileIds,
        bool steer,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (FindInvalidRequest(text, fileIds) is { } invalid)
        {
            return new MessagePost(null, null, PostProblem.Invalid, invalid);
        }

        Message message;
        Run run;
        IReadOnlyList<RunEvent> appended = [];

        await using (var work = await scope.Begin(cancellationToken))
        {
            var thread = await threads.GetThreadForUpdate(threadId, cancellationToken);

            if (thread is null)
            {
                return new MessagePost(null, null, PostProblem.NotFound);
            }

            var bot = await bots.FindBot(thread.BotId, cancellationToken);
            var attached = await TakeFiles(thread, bot, fileIds, userId, cancellationToken);

            if (attached.Problem is not null)
            {
                return new MessagePost(null, null, PostProblem.Invalid, attached.Problem);
            }

            message = NewUserMessage(text, attached.Files, userId);
            thread.LastActivityAt = time.GetUtcNow();

            if (await runs.FindActiveRun(threadId, cancellationToken) is { } active)
            {
                message.Steers = steer && active.PrincipalUserId == userId;
                messages.AddPending(thread, message);
                run = active;
            }
            else
            {
                if (bot is not { Status: BotStatus.Active })
                {
                    return new MessagePost(null, null, PostProblem.Invalid, "This bot is paused or removed, so it cannot answer.");
                }

                await messages.AppendMessages(thread, [message], cancellationToken);
                TitleFromFirstMessage(thread, string.IsNullOrWhiteSpace(text) ? attached.Files[0].Name : text);
                (run, appended) = await queue.Queue(thread, message, userId, cancellationToken);
            }

            foreach (var file in attached.Files)
            {
                file.MessageId = message.Id;
            }

            await work.Commit(cancellationToken);
        }

        hub.Publish(run.RootRunId, appended);
        signal.Wake();
        Tell(run, UserSignalKinds.ThreadChanged);
        logger.LogInformation("Message {MessageId} stored for thread {ThreadId}; run {RunId} answers it", message.Id, threadId, run.Id);
        return new MessagePost(message, run);
    }

    /// <summary>
    /// Asks a run and the runs below it to stop. The run's own person may, and so may a space admin. A run no worker
    /// holds (queued or waiting) is ended here and now when the caller can see its thread. Any other is ended by a
    /// worker acting as the run's own person: a running one within its watch interval, and one whose private thread
    /// the caller cannot see as soon as a worker claims it, which a cancel request makes possible at once.
    /// </summary>
    public async Task<RunCancel> Cancel(Guid runId, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        Run? run;
        IReadOnlyList<RunEvent> appended = [];
        QueuedRun? next = null;

        await using (var work = await scope.Begin(cancellationToken))
        {
            run = await runs.GetRunForUpdate(runId, cancellationToken);

            if (run is null)
            {
                return new RunCancel(CancelOutcome.NotFound);
            }

            // Run first, thread second: the order a worker takes the same two locks in when it ends a run.
            var thread = await threads.GetThreadForUpdate(run.ThreadId, cancellationToken);

            if (thread is null && !isSpaceAdmin)
            {
                return new RunCancel(CancelOutcome.NotFound);
            }

            if (run.PrincipalUserId != userId && !isSpaceAdmin)
            {
                return new RunCancel(CancelOutcome.Forbidden);
            }

            if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
            {
                return new RunCancel(CancelOutcome.Requested, run);
            }

            await runs.RequestCancel(runId, cancellationToken);
            run.CancelRequestedAt ??= time.GetUtcNow();

            if (run.ParentRunId is not null)
            {
                await DenyWaitingApprovalsBeneath(run, cancellationToken);
            }

            if (run is { Status: RunStatus.Queued or RunStatus.Waiting, ParentRunId: null } && thread is not null)
            {
                run.Status = RunStatus.Cancelled;
                run.StatusReason = RunReasons.CancelledByUser;
                run.FinishedAt = time.GetUtcNow();
                await runs.AddToSubtreeUsage(run.Id, run.Usage, cancellationToken);
                appended = [.. await events.Append(run.RootRunId, thread, [CancelledEvent(run)], cancellationToken), .. await queue.EndOpenDescendants(run, cancellationToken)];
                next = await queue.RunEnded(run, thread, cancellationToken);
            }

            await runs.SignalWorkers(cancellationToken);
            await work.Commit(cancellationToken);
        }

        signal.RequestCancel(runId);
        signal.Wake();
        hub.Publish(run.RootRunId, appended);

        if (appended.Count > 0)
        {
            Tell(run, UserSignalKinds.RunFinished);
        }

        if (next is not null)
        {
            hub.Publish(next.Run.RootRunId, next.Events);
            Tell(next.Run, UserSignalKinds.ThreadChanged);
        }

        logger.LogInformation("Cancel requested for run {RunId}; status is now {Status}", runId, run.Status);
        return new RunCancel(CancelOutcome.Requested, run);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// A child run that waits for a person holds its whole tree. When it is cancelled, the approvals it and the runs
    /// beneath it wait for are denied, so the tree can be taken up again: the child then ends as cancelled, and the
    /// run above it gets a "cancelled" result and goes on.
    /// </summary>
    private async Task DenyWaitingApprovalsBeneath(Run child, CancellationToken cancellationToken)
    {
        foreach (var run in await runs.ListSelfAndDescendants(child.Id, cancellationToken))
        {
            if (run.Status is not RunStatus.Waiting)
            {
                continue;
            }

            foreach (var call in await toolCalls.ListPendingApprovals(run.ThreadId, cancellationToken))
            {
                ToolDecisions.Deny(call, CancelledBeforeApproval, time.GetUtcNow());
            }
        }
    }

    /// <summary>Hints to the run's own person, on any of their screens, that the thread changed.</summary>
    private void Tell(Run run, string kind)
    {
        if (tenant.SpaceId is { } spaceId)
        {
            signals.Publish(spaceId, run.PrincipalUserId, new UserSignal(kind, run.ThreadId, run.Id));
        }
    }

    private Message NewUserMessage(string text, IReadOnlyList<StoredFile> attached, Guid userId) => new()
    {
        Role = MessageRole.User,
        AuthorUserId = userId,
        Parts =
        [
            .. string.IsNullOrWhiteSpace(text) ? Array.Empty<MessagePart>() : [new TextPart(text)],
            .. attached.Select(ToPart),
        ],
        CreatedAt = time.GetUtcNow(),
    };

    private static MessagePart ToPart(StoredFile file) =>
        FileInspector.KindOf(file.ContentType) is FileKind.Image
            ? new ImagePart(file.Id, file.ContentType, null, file.Name, file.Size, file.Width, file.Height)
            : new FilePart(file.Id, file.Name, file.ContentType, file.Size, file.Preview);

    /// <summary>Why the request cannot be a message, before anything is read: no words and no files, too long, too many files.</summary>
    private string? FindInvalidRequest(string text, IReadOnlyList<Guid> fileIds)
    {
        if (text.Length > MaxMessageLength || (string.IsNullOrWhiteSpace(text) && fileIds.Count == 0))
        {
            return $"Write a message of up to {MaxMessageLength} characters.";
        }

        if (fileIds.Count > deployment.Value.Rules.MaxFilesPerMessage)
        {
            return FileWords.TooMany(deployment.Value.Rules.MaxFilesPerMessage);
        }

        return fileIds.Distinct().Count() != fileIds.Count ? "The same file is attached twice." : null;
    }

    /// <summary>
    /// The files to send, tracked and in the order asked for: each one the sender's own, uploaded to this thread, not
    /// sent before, and one the bot can open now. Row-level security has already hidden any file the sender cannot see.
    /// </summary>
    private async Task<AttachedFiles> TakeFiles(
        ChatThread thread,
        Bot? bot,
        IReadOnlyList<Guid> fileIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
        {
            return new AttachedFiles([], null);
        }

        var found = await files.GetFilesForUpdate(fileIds, cancellationToken);
        List<StoredFile> ordered = [.. fileIds.Select(id => found.FirstOrDefault(file => file.Id == id)).OfType<StoredFile>()];

        if (ordered.Count != fileIds.Count
            || ordered.Any(file => file.ThreadId != thread.Id || file.MessageId is not null || file.CreatedByUserId != userId))
        {
            return new AttachedFiles([], "A file on this message is not one you attached in this conversation, or it was sent already. Attach it again.");
        }

        if (bot is null)
        {
            return new AttachedFiles([], "This bot is paused or removed, so it cannot answer.");
        }

        var can = await abilities.Read(bot, cancellationToken);
        var refused = ordered
            .Select(file => (file.Name, Refusal: FileUses.RefusalFor(FileInspector.KindOf(file.ContentType), can)))
            .FirstOrDefault(pair => pair.Refusal is not null);

        return refused.Refusal is { } refusal
            ? new AttachedFiles([], $"{refused.Name}: {FileWords.CannotOpen(refusal, bot.Name)}")
            : new AttachedFiles(ordered, null);
    }

    /// <summary>A side chat is named after its first message. A main thread is the bot's conversation and takes no title.</summary>
    private static void TitleFromFirstMessage(ChatThread thread, string text)
    {
        if (!thread.IsMain)
        {
            thread.Title ??= ThreadService.Shorten(text.Trim()[..Math.Min(text.Trim().Length, TitleLength)]);
        }
    }

    private static NewRunEvent CancelledEvent(Run run) =>
        RunEventWriter.Event(
            run.Id,
            RunEventTypes.RunCancelled,
            new RunStatusChanged(RunStatus.Cancelled.ToWireName(), run.StatusReason, null, run.Attempt, run.Usage.ToTotals()));

    #endregion

    #region Nested Types

    private sealed record AttachedFiles(IReadOnlyList<StoredFile> Files, string? Problem);

    #endregion
}


/// <summary>The short codes that explain a run's status, from docs/architecture/03-run-lifecycle.md.</summary>
public static class RunReasons
{
    #region Reasons

    public const string AwaitingApproval = "awaiting_approval";

    public const string StepLimit = "step_limit";

    public const string CancelledByUser = "cancelled_by_user";

    public const string LeaseLost = "lease_lost";

    public const string CancelledByParent = "cancelled_by_parent";

    public const string LoopDetected = "loop_detected";

    public const string BudgetExceeded = "budget_exceeded";

    #endregion
}
