namespace Drover.Engine.Runs;

public enum PostProblem
{
    None,
    NotFound,
    Invalid,
    Busy,
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
    RunRepository runs,
    RunEventRepository events,
    TenantScope scope,
    RunSignal signal,
    RunEventHub hub,
    TimeProvider time,
    ILogger<RunService> logger)
{
    #region Private Fields

    public const int MaxMessageLength = 100_000;

    private const int TitleLength = 80;

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
    /// Stores the message, queues a run for it, and appends <c>run.queued</c>, in one transaction; then wakes the
    /// workers. A thread runs one root run at a time, so a message sent while one is active is refused: the person
    /// waits for it or cancels it and sends again. That keeps the order of a thread unambiguous.
    /// </summary>
    public async Task<MessagePost> PostMessage(Guid threadId, string text, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxMessageLength)
        {
            return new MessagePost(null, null, PostProblem.Invalid, $"Write a message of up to {MaxMessageLength} characters.");
        }

        Message message;
        Run run;
        IReadOnlyList<RunEvent> appended;

        await using (var work = await scope.Begin(cancellationToken))
        {
            var thread = await threads.GetThreadForUpdate(threadId, cancellationToken);

            if (thread is null)
            {
                return new MessagePost(null, null, PostProblem.NotFound);
            }

            if (await runs.FindActiveRun(threadId, cancellationToken) is not null)
            {
                return new MessagePost(null, null, PostProblem.Busy, "The bot is still working on the last message. Wait for it, or stop it and send again.");
            }

            if (await bots.FindBot(thread.BotId, cancellationToken) is not { Status: BotStatus.Active })
            {
                return new MessagePost(null, null, PostProblem.Invalid, "This bot is paused or removed, so it cannot answer.");
            }

            message = NewUserMessage(text, userId);
            await messages.AppendMessages(thread, [message], cancellationToken);
            run = NewRun(thread, message, userId);
            runs.Add(run);
            thread.LastActivityAt = time.GetUtcNow();
            thread.Title ??= ThreadService.Shorten(text.Trim()[..Math.Min(text.Trim().Length, TitleLength)]);
            appended = await events.Append(run.Id, thread, [QueuedEvent(run)], cancellationToken);
            await runs.SignalWorkers(cancellationToken);
            await work.Commit(cancellationToken);
        }

        hub.Publish(run.RootRunId, appended);
        signal.Wake();
        logger.LogInformation("Run {RunId} queued for thread {ThreadId}", run.Id, threadId);
        return new MessagePost(message, run);
    }

    /// <summary>
    /// Asks a run and the runs below it to stop. The run's own person may, and so may a space admin. A run no worker
    /// holds (queued or waiting) is cancelled here and now when the caller can see its thread; a running one is
    /// stopped by its worker, which sees the request within its watch interval.
    /// </summary>
    public async Task<RunCancel> Cancel(Guid runId, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        Run? run;
        IReadOnlyList<RunEvent> appended = [];

        await using (var work = await scope.Begin(cancellationToken))
        {
            run = await runs.GetRunForUpdate(runId, cancellationToken);

            if (run is null)
            {
                return new RunCancel(CancelOutcome.NotFound);
            }

            var thread = await threads.FindThread(run.ThreadId, cancellationToken);

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

            // An admin who cannot see a private thread cannot write its events; the run's worker does it as its person.
            if (run is { Status: RunStatus.Queued or RunStatus.Waiting, ParentRunId: null } && thread is not null)
            {
                run.Status = RunStatus.Cancelled;
                run.StatusReason = RunReasons.CancelledByUser;
                run.FinishedAt = time.GetUtcNow();
                appended = await events.Append(run.RootRunId, thread, [CancelledEvent(run)], cancellationToken);
            }
            await work.Commit(cancellationToken);
        }

        signal.RequestCancel(runId);
        signal.Wake();
        hub.Publish(run.RootRunId, appended);
        logger.LogInformation("Cancel requested for run {RunId}; status is now {Status}", runId, run.Status);
        return new RunCancel(CancelOutcome.Requested, run);
    }

    #endregion

    #region Private Methods

    private Message NewUserMessage(string text, Guid userId) => new()
    {
        Role = MessageRole.User,
        AuthorUserId = userId,
        Parts = [new TextPart(text)],
        CreatedAt = time.GetUtcNow(),
    };

    private static Run NewRun(ChatThread thread, Message message, Guid userId)
    {
        var run = new Run
        {
            ThreadId = thread.Id,
            BotId = thread.BotId,
            PrincipalUserId = userId,
            Trigger = RunTrigger.Message,
            TriggerMessageId = message.Id,
        };
        run.RootRunId = run.Id;
        return run;
    }

    private static NewRunEvent QueuedEvent(Run run) =>
        RunEventWriter.Event(run.Id, RunEventTypes.RunQueued, new RunStatusChanged(RunStatus.Queued.ToWireName(), null, null, run.Attempt, null));

    private static NewRunEvent CancelledEvent(Run run) =>
        RunEventWriter.Event(
            run.Id,
            RunEventTypes.RunCancelled,
            new RunStatusChanged(RunStatus.Cancelled.ToWireName(), run.StatusReason, null, run.Attempt, run.Usage.ToTotals()));

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

    #endregion
}
