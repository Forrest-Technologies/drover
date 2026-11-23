namespace Drover.Server.Runs;

/// <summary>
/// Runs of a space, and the approvals they wait on. Reading a run runs under the tenant scope filter. Cancelling one
/// and answering an approval open their own scope in the service, because both tell the workers after the commit.
/// </summary>
public static class RunEndpoints
{
    #region Private Fields

    private const string NotAHandover = "This call does not ask a person to take over the screen.";

    private const string ButtonHeldWhat =
        "A mouse button on this screen could not be let go of, so the screen was not handed back.";

    private const string ButtonHeldSentence = $"{ButtonHeldWhat} Try again, or rebuild the sandbox.";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapRunEndpoints(this RouteGroupBuilder space)
    {
        space.MapGroup("/runs").AddEndpointFilter<TenantScopeFilter>()
            .MapGet("/{runId:guid}", GetRun).WithName("GetRun");
        space.MapPost("/runs/{runId:guid}/cancel", CancelRun)
            .WithName("CancelRun")
            .WithSummary("Asks the run and the runs below it to stop. The run's own person or a space admin may.");
        space.MapPost("/runs/{runId:guid}/retry", RetryRun)
            .WithName("RetryRun")
            .WithSummary("Starts a new run from the message a failed run answered. Only the run's own person may, while it is their newest run in the thread.");
        space.MapPost("/tool-calls/{toolCallId:guid}/decision", DecideToolCall)
            .WithName("DecideToolCall")
            .WithSummary("Approves or denies a tool call that waits for a person. The run's own person or a space admin may, if they can see the call.");
        space.MapPost("/tool-calls/{toolCallId:guid}/takeover", TakeOver)
            .WithName("TakeOverScreen")
            .WithSummary("Records that a person took over a bot's screen because its call asked them to. An owner or admin who can see the call may.");
        space.MapPost("/tool-calls/{toolCallId:guid}/handback", HandBack)
            .WithName("HandBackScreen")
            .WithSummary("Answers a call that asked a person to take over the screen: done, or could not with an optional line for the bot. The run's own person or a space admin may, if they can see the call.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<RunSummary>, NotFound>> GetRun(Guid runId, HttpContext context, RunService runs) =>
        await runs.FindRun(runId, context.GetSpaceAccess().Role <= MembershipRole.Admin, context.RequestAborted) is { } run
            ? TypedResults.Ok(run.ToSummary())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<RunSummary>, NotFound, ForbidHttpResult>> CancelRun(
        Guid runId,
        HttpContext context,
        RunService runs)
    {
        var access = context.GetSpaceAccess();
        var cancel = await runs.Cancel(runId, access.UserId, access.Role <= MembershipRole.Admin, context.RequestAborted);

        return cancel.Outcome switch
        {
            CancelOutcome.NotFound => TypedResults.NotFound(),
            CancelOutcome.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.Ok(cancel.Run!.ToSummary()),
        };
    }

    private static async Task<Results<Accepted<RunSummary>, NotFound, ForbidHttpResult, ProblemHttpResult>> RetryRun(
        Guid runId,
        HttpContext context,
        RunService runs)
    {
        var access = context.GetSpaceAccess();
        var retry = await runs.Retry(runId, access.UserId, context.RequestAborted);

        return retry.Outcome switch
        {
            RetryOutcome.NotFound => TypedResults.NotFound(),
            RetryOutcome.Forbidden => TypedResults.Forbid(),
            RetryOutcome.NotRetryable => TypedResults.Problem(
                title: "This run cannot be tried again: something came after it, or it did not fail.",
                statusCode: StatusCodes.Status409Conflict),
            RetryOutcome.BotUnavailable => TypedResults.Problem(
                title: "This bot is paused or removed, so it cannot answer.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Accepted($"/api/spaces/{access.SpaceId}/runs/{retry.Run!.Id}", retry.Run.ToSummary()),
        };
    }

    private static async Task<Results<Ok<RunSummary>, NotFound, ForbidHttpResult, ProblemHttpResult>> DecideToolCall(
        Guid toolCallId,
        ToolDecisionRequest request,
        HttpContext context,
        ApprovalService approvals)
    {
        var access = context.GetSpaceAccess();
        var decision = await approvals.Decide(
            toolCallId,
            request.Approved,
            request.Reason,
            access.UserId,
            access.Role <= MembershipRole.Admin,
            context.RequestAborted);

        return ToResult(decision, "This call asks a person to take over the screen. Answer it with done or could not.");
    }

    private static async Task<Results<Ok<RunSummary>, NotFound, ForbidHttpResult, ProblemHttpResult>> TakeOver(
        Guid toolCallId,
        HttpContext context,
        ApprovalService approvals)
    {
        var access = context.GetSpaceAccess();
        var taken = await approvals.TakeOver(toolCallId, access.UserId, access.Role <= MembershipRole.Admin, context.RequestAborted);
        return ToResult(taken, NotAHandover);
    }

    private static async Task<Results<Ok<RunSummary>, NotFound, ForbidHttpResult, ProblemHttpResult>> HandBack(
        Guid toolCallId,
        ToolHandbackRequest request,
        HttpContext context,
        ApprovalService approvals)
    {
        var access = context.GetSpaceAccess();
        var answer = await approvals.HandBack(
            toolCallId,
            request.Done,
            request.Note,
            access.UserId,
            access.Role <= MembershipRole.Admin,
            context.RequestAborted);
        return ToResult(answer, NotAHandover);
    }

    #endregion

    #region Private Methods

    private static Results<Ok<RunSummary>, NotFound, ForbidHttpResult, ProblemHttpResult> ToResult(ToolDecision decision, string wrongKind) =>
        decision.Outcome switch
        {
            DecisionOutcome.NotFound => TypedResults.NotFound(),
            DecisionOutcome.Forbidden => TypedResults.Forbid(),
            DecisionOutcome.NotWaiting => TypedResults.Problem(title: "This call is not waiting for an answer any more.", statusCode: StatusCodes.Status409Conflict),
            DecisionOutcome.WrongKind => TypedResults.Problem(title: wrongKind, statusCode: StatusCodes.Status409Conflict),
            DecisionOutcome.ButtonHeld => TypedResults.Problem(
                title: ButtonHeldSentence,
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(decision.Run!.ToSummary()),
        };

    #endregion
}
