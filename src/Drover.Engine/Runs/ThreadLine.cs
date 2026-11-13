namespace Drover.Engine.Runs;

/// <summary>A run that was just queued, and the events to publish once its transaction has committed.</summary>
public sealed record QueuedRun(Run Run, IReadOnlyList<RunEvent> Events);

/// <summary>
/// A thread's line of work. A thread has one root run at a time. A message sent while that run works is stored as
/// pending: it has no position, so it is not yet part of the conversation the model reads. When the run ends, the
/// oldest pending message starts the next run, in the same transaction, so a message is never left with nothing to
/// answer it. Callers hold the lock on the thread row, which is what makes "is a run active" and "is a message
/// pending" one question.
/// </summary>
public sealed class ThreadLine(
    MessageRepository messages,
    RunRepository runs,
    RunEventRepository events,
    ToolCallRepository toolCalls,
    TimeProvider time)
{
    #region Private Fields

    private const string EndedBeforeApproval = "The run ended before this call was approved.";

    #endregion

    #region Public Methods

    /// <summary>Queues the run that answers a message, appends <c>run.queued</c>, and wakes the workers at commit.</summary>
    public async Task<QueuedRun> Queue(ChatThread thread, Message trigger, Guid principalUserId, CancellationToken cancellationToken)
    {
        var run = new Run
        {
            ThreadId = thread.Id,
            BotId = thread.BotId,
            PrincipalUserId = principalUserId,
            Trigger = RunTrigger.Message,
            TriggerMessageId = trigger.Id,
        };
        run.RootRunId = run.Id;
        runs.Add(run);
        var queued = RunEventWriter.Event(
            run.Id,
            RunEventTypes.RunQueued,
            new RunStatusChanged(RunStatus.Queued.ToWireName(), null, null, run.Attempt, null));
        var stored = await events.Append(run.Id, thread, [queued], cancellationToken);
        await runs.SignalWorkers(cancellationToken);
        return new QueuedRun(run, stored);
    }

    /// <summary>
    /// Gives the run's pending messages their place in the conversation: the one that started it and every other
    /// pending message of the same person, in the order they were sent. A run acts for one person, so it takes that
    /// person's messages only; another member's wait for a run of their own. Does nothing once the run's own message
    /// has a position.
    /// </summary>
    public async Task<IReadOnlyList<Message>> PlaceMessagesOf(Run run, ChatThread thread, CancellationToken cancellationToken)
    {
        var pending = await messages.ListPendingMessages(thread.Id, cancellationToken);

        if (run.ParentRunId is not null || pending.All(message => message.Id != run.TriggerMessageId))
        {
            return [];
        }

        List<Message> taken = [.. pending.Where(message => message.AuthorUserId == run.PrincipalUserId)];

        foreach (var message in taken)
        {
            message.RunId = run.Id;
        }

        await messages.AppendMessages(thread, taken, cancellationToken);
        return taken;
    }

    /// <summary>
    /// A root run has ended for good. Its own messages join the conversation if they never did, approvals nobody
    /// answered are denied, and the oldest message still pending starts the next run. Null when nothing is pending.
    /// </summary>
    public async Task<QueuedRun?> RunEnded(Run run, ChatThread thread, CancellationToken cancellationToken)
    {
        if (run.ParentRunId is not null)
        {
            return null;
        }

        await PlaceMessagesOf(run, thread, cancellationToken);

        foreach (var call in await toolCalls.ListPendingApprovals(thread.Id, cancellationToken))
        {
            ToolDecisions.Deny(call, EndedBeforeApproval, time.GetUtcNow());
        }

        // The query answers from the database, where the messages placed a moment ago are still pending; the rows
        // in hand know better.
        var pending = await messages.ListPendingMessages(thread.Id, cancellationToken);

        // A pending message a rule withheld joins the conversation where people read it and starts nothing.
        await messages.AppendMessages(thread, [.. pending.Where(message => message is { Position: null, Withheld: true })], cancellationToken);
        return pending.FirstOrDefault(message => message.Position is null && message.AuthorUserId is not null) is { AuthorUserId: { } author } next
            ? await Queue(thread, next, author, cancellationToken)
            : null;
    }

    #endregion
}

/// <summary>What a decision does to a <c>tool_calls</c> row. A denial is stored as the call's result, so the model is told.</summary>
public static class ToolDecisions
{
    #region Public Methods

    public static void Deny(ToolCall call, string reason, DateTimeOffset now)
    {
        call.Status = ToolCallStatus.Denied;
        call.Result = JsonSerializer.SerializeToElement(new { error = reason });
        call.IsError = true;
        call.FinishedAt = now;
    }

    #endregion
}
