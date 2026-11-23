namespace Drover.Engine.Runs;

public enum DecisionOutcome
{
    Recorded,
    NotFound,
    Forbidden,
    NotWaiting,

    /// <summary>
    /// The call is not answered this way: a request for a person at the screen is answered with done or could not,
    /// and any other call with approve or deny.
    /// </summary>
    WrongKind,

    /// <summary>
    /// The screen was not handed back: a mouse button a person holds on it could not be let go of, and a screen is
    /// never handed back with a button down. The call still waits.
    /// </summary>
    ButtonHeld,
}

public sealed record ToolDecision(DecisionOutcome Outcome, Run? Run = null);

/// <summary>
/// A person's answer to a tool call that waits for them: an approval, or a bot's request that a person take over its
/// screen. The answer is stored on the call's row and in the run's log; the run becomes claimable once every call it
/// waits on has one, and the worker that takes it passes the answers on. Opens its own tenant scope, because it wakes
/// the workers after the commit.
/// </summary>
public sealed class ApprovalService(
    ToolCallRepository toolCalls,
    RunRepository runs,
    ThreadRepository threads,
    RunEventRepository events,
    AuditLog audit,
    TenantScope scope,
    RunSignal signal,
    RunEventHub hub,
    UserSignalHub signals,
    IEnumerable<SandboxBroker> brokers,
    RunScopes runScopes,
    TimeProvider time,
    ILogger<ApprovalService> logger)
{
    #region Private Fields

    public const int MaxReasonLength = 1000;

    private const string Declined = "The person declined this call.";

    #endregion

    #region Public Methods

    /// <summary>
    /// Approves or denies the call. Only someone who can see the call may answer it, and of those only the person
    /// the run acts for or a space admin: nobody approves a call whose arguments they cannot read, and a member
    /// cannot approve what a bot does with another member's authority.
    /// </summary>
    public Task<ToolDecision> Decide(
        Guid toolCallId,
        bool approved,
        string? reason,
        Guid userId,
        bool isSpaceAdmin,
        CancellationToken cancellationToken) =>
        Answer(
            new Answering(toolCallId, userId, isSpaceAdmin, Handover: false),
            (call, now) =>
            {
                if (approved)
                {
                    call.Status = ToolCallStatus.Approved;
                }
                else
                {
                    ToolDecisions.Deny(call, Reason(reason), now);
                }

                return approved;
            },
            cancellationToken);

    /// <summary>
    /// The person opens the bot's screen to do what the bot asked. Nothing about the call changes: the audit log
    /// records that they took over. Only owners and admins use a bot's screen, and of those only one who can see the
    /// call; a member whose bot asks is told so by the screen, and answers from the card.
    /// </summary>
    public async Task<ToolDecision> TakeOver(Guid toolCallId, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var found = await Find(toolCallId, userId, isSpaceAdmin, cancellationToken);

        if (found.Refusal is { } refusal)
        {
            return new ToolDecision(refusal);
        }

        if (!isSpaceAdmin)
        {
            return new ToolDecision(DecisionOutcome.Forbidden);
        }

        var root = await runs.FindRun(found.Run!.RootRunId, cancellationToken);

        if (!ComputerTools.IsHandover(found.Call!.ToolName))
        {
            return new ToolDecision(DecisionOutcome.WrongKind, root);
        }

        if (root is not { Status: RunStatus.Waiting, CancelRequestedAt: null } || found.Call.Status is not ToolCallStatus.PendingApproval)
        {
            return new ToolDecision(DecisionOutcome.NotWaiting, root);
        }

        audit.Record(userId, "sandbox.taken_over", AuditTargets.Bot, found.Run.BotId, null, null);
        await work.Commit(cancellationToken);
        logger.LogInformation("A person took over the screen for tool call {ToolCallId} of run {RunId}", toolCallId, found.Run.Id);
        return new ToolDecision(DecisionOutcome.Recorded, root);
    }

    /// <summary>
    /// The person hands the screen back: done, or could not, with a line for the bot that may be empty. Answered by
    /// the same people and in the same way as an approval: done lets the call run, and its result says so; could not
    /// ends it with the person's line as its result. The screen is no longer held once the answer is stored. The answer
    /// is stored under the gate of the screen it hands back, once every mouse button a person holds on it is let go of
    /// and any input under way on it has ended; input that waited behind it is refused.
    /// </summary>
    public async Task<ToolDecision> HandBack(
        Guid toolCallId,
        bool done,
        string? note,
        Guid userId,
        bool isSpaceAdmin,
        CancellationToken cancellationToken)
    {
        Task<ToolDecision> Answering() => Answer(
            new Answering(toolCallId, userId, isSpaceAdmin, Handover: true),
            (call, now) =>
            {
                if (done)
                {
                    call.Status = ToolCallStatus.Approved;
                }
                else
                {
                    call.Status = ToolCallStatus.Denied;
                    call.Result = ComputerTools.CouldNot(Trimmed(note));
                    call.IsError = false;
                    call.FinishedAt = now;
                }

                return done;
            },
            cancellationToken);

        if (brokers.SingleOrDefault() is not { } broker
            || await FindScreen(toolCallId, userId, isSpaceAdmin, cancellationToken) is not { } screen)
        {
            return await Answering();
        }

        return await broker.HandOver(
            screen,
            Answering,
            () => new ToolDecision(DecisionOutcome.ButtonHeld),
            cancellationToken);
    }

    #endregion

    #region Answering

    private async Task<ToolDecision> Answer(Answering answering, Func<ToolCall, DateTimeOffset, bool> settle, CancellationToken cancellationToken)
    {
        Run? root;
        IReadOnlyList<RunEvent> appended;
        bool approved;

        await using (var work = await scope.Begin(cancellationToken))
        {
            var found = await Find(answering.ToolCallId, answering.UserId, answering.IsSpaceAdmin, cancellationToken);

            if (found.Refusal is { } refusal)
            {
                return new ToolDecision(refusal);
            }

            var run = found.Run!;

            // The root row is locked before the call is read for good, so two answers to one call take turns.
            root = await runs.GetRunForUpdate(run.RootRunId, cancellationToken);
            var call = await toolCalls.GetToolCallForUpdate(answering.ToolCallId, cancellationToken);

            if (root is not { Status: RunStatus.Waiting, CancelRequestedAt: null } || call is not { Status: ToolCallStatus.PendingApproval })
            {
                return new ToolDecision(DecisionOutcome.NotWaiting, root);
            }

            if (ComputerTools.IsHandover(call.ToolName) != answering.Handover)
            {
                return new ToolDecision(DecisionOutcome.WrongKind, root);
            }

            var now = time.GetUtcNow();
            call.DecidedByUserId = answering.UserId;
            call.DecidedAt = now;
            approved = settle(call, now);

            if (answering.Handover)
            {
                await ReleaseScreen(root, run, call, cancellationToken);
                audit.Record(answering.UserId, "sandbox.handed_back", AuditTargets.Bot, run.BotId, null, null, new JsonObject { ["outcome"] = approved ? "done" : "could_not" });
            }

            var decided = RunEventWriter.Event(
                run.Id,
                approved ? RunEventTypes.ToolApproved : RunEventTypes.ToolDenied,
                new ToolDecided(call.Id, answering.UserId));
            appended = await events.Append(root.Id, found.Thread!, [decided], cancellationToken);
            await runs.SignalWorkers(cancellationToken);
            await work.Commit(cancellationToken);
        }

        hub.Publish(root.Id, appended);
        signal.Wake();

        // The run's person sees on every screen that the call no longer waits for them. A delegated child's call
        // waits in the root's tree, and the root's thread is the one that reads as waiting, so that is the one named.
        signals.Publish(root.SpaceId, root.PrincipalUserId, new UserSignal(UserSignalKinds.ThreadChanged, root.ThreadId, root.Id));
        logger.LogInformation("Tool call {ToolCallId} of run {RunId} was {Decision}", answering.ToolCallId, root.Id, approved ? "approved" : "denied");
        return new ToolDecision(DecisionOutcome.Recorded, root);
    }

    /// <summary>
    /// The screen a handover call would give back, as the bot that asked reaches it, or null when this person cannot
    /// answer the call at all. Read in its own short transaction, before the screen's gate is taken.
    /// </summary>
    private async Task<SandboxCaller?> FindScreen(
        Guid toolCallId,
        Guid userId,
        bool isSpaceAdmin,
        CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var found = await Find(toolCallId, userId, isSpaceAdmin, cancellationToken);

        return found is { Refusal: null, Run: { } run, Call: { } call } && ComputerTools.IsHandover(call.ToolName)
            ? new SandboxCaller(runScopes.For(run.SpaceId, userId), run.BotId)
            : null;
    }

    /// <summary>
    /// The call, its run, and its thread, as this person sees them, or why they may not answer: not visible at all, or
    /// visible to someone who is neither the run's person nor a space admin.
    /// </summary>
    private async Task<Found> Find(Guid toolCallId, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        var visible = (await toolCalls.ListToolCalls([toolCallId], cancellationToken)).SingleOrDefault();
        var run = visible is null ? null : await runs.FindRun(visible.RunId, cancellationToken);
        var thread = visible is null ? null : await threads.FindThread(visible.ThreadId, cancellationToken);

        if (visible is null || run is null || thread is null)
        {
            return new Found(DecisionOutcome.NotFound);
        }

        return run.PrincipalUserId != userId && !isSpaceAdmin
            ? new Found(DecisionOutcome.Forbidden)
            : new Found(null, visible, run, thread);
    }

    /// <summary>
    /// The run no longer waits for a person at the screen, unless it asked for one again in the same step, so the
    /// bots that share the screen may use it again. It still waits for its other calls, as for any approval.
    /// </summary>
    private async Task ReleaseScreen(Run root, Run run, ToolCall answered, CancellationToken cancellationToken)
    {
        var asking = run.Id == root.Id ? root : await runs.GetRunForUpdate(run.Id, cancellationToken);

        if (asking is not { Status: RunStatus.Waiting, StatusReason: RunReasons.AwaitingTakeover })
        {
            return;
        }

        var stillAsks = (await toolCalls.ListPendingApprovals(answered.ThreadId, cancellationToken))
            .Any(other => other.Id != answered.Id && other.RunId == run.Id && ComputerTools.IsHandover(other.ToolName));

        if (!stillAsks)
        {
            asking.StatusReason = RunReasons.AwaitingApproval;
        }
    }

    #endregion

    #region Private Methods

    private static string Reason(string? reason) =>
        Trimmed(reason) is { } said ? $"{Declined} They said: {said}" : Declined;

    private static string? Trimmed(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, MaxReasonLength)];

    #endregion

    #region Nested Types

    private sealed record Answering(Guid ToolCallId, Guid UserId, bool IsSpaceAdmin, bool Handover);

    private sealed record Found(DecisionOutcome? Refusal, ToolCall? Call = null, Run? Run = null, ChatThread? Thread = null);

    #endregion
}
