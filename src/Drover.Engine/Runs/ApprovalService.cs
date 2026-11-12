namespace Drover.Engine.Runs;

public enum DecisionOutcome
{
    Recorded,
    NotFound,
    Forbidden,
    NotWaiting,
}

public sealed record ToolDecision(DecisionOutcome Outcome, Run? Run = null);

/// <summary>
/// A person's answer to a tool call that waits for approval. The answer is stored on the call's row and in the run's
/// log; the run becomes claimable once every call it waits on has one, and the worker that takes it passes the
/// answers on. Opens its own tenant scope, because it wakes the workers after the commit.
/// </summary>
public sealed class ApprovalService(
    ToolCallRepository toolCalls,
    RunRepository runs,
    ThreadRepository threads,
    RunEventRepository events,
    TenantScope scope,
    RunSignal signal,
    RunEventHub hub,
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
    public async Task<ToolDecision> Decide(
        Guid toolCallId,
        bool approved,
        string? reason,
        Guid userId,
        bool isSpaceAdmin,
        CancellationToken cancellationToken)
    {
        Run? root;
        IReadOnlyList<RunEvent> appended;

        await using (var work = await scope.Begin(cancellationToken))
        {
            var visible = (await toolCalls.ListToolCalls([toolCallId], cancellationToken)).SingleOrDefault();
            var run = visible is null ? null : await runs.FindRun(visible.RunId, cancellationToken);
            var thread = visible is null ? null : await threads.FindThread(visible.ThreadId, cancellationToken);

            if (visible is null || run is null || thread is null)
            {
                return new ToolDecision(DecisionOutcome.NotFound);
            }

            if (run.PrincipalUserId != userId && !isSpaceAdmin)
            {
                return new ToolDecision(DecisionOutcome.Forbidden);
            }

            // The root row is locked before the call is read for good, so two answers to one call take turns.
            root = await runs.GetRunForUpdate(run.RootRunId, cancellationToken);
            var call = await toolCalls.GetToolCallForUpdate(toolCallId, cancellationToken);

            if (root is not { Status: RunStatus.Waiting, CancelRequestedAt: null } || call is not { Status: ToolCallStatus.PendingApproval })
            {
                return new ToolDecision(DecisionOutcome.NotWaiting, root);
            }

            var now = time.GetUtcNow();
            call.DecidedByUserId = userId;
            call.DecidedAt = now;

            if (approved)
            {
                call.Status = ToolCallStatus.Approved;
            }
            else
            {
                ToolDecisions.Deny(call, Reason(reason), now);
            }

            var decided = RunEventWriter.Event(
                run.Id,
                approved ? RunEventTypes.ToolApproved : RunEventTypes.ToolDenied,
                new ToolDecided(call.Id, userId));
            appended = await events.Append(root.Id, thread, [decided], cancellationToken);
            await runs.SignalWorkers(cancellationToken);
            await work.Commit(cancellationToken);
        }

        hub.Publish(root.Id, appended);
        signal.Wake();
        logger.LogInformation("Tool call {ToolCallId} of run {RunId} was {Decision}", toolCallId, root.Id, approved ? "approved" : "denied");
        return new ToolDecision(DecisionOutcome.Recorded, root);
    }

    #endregion

    #region Private Methods

    private static string Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? Declined : $"{Declined} They said: {reason.Trim()[..Math.Min(reason.Trim().Length, MaxReasonLength)]}";

    #endregion
}
