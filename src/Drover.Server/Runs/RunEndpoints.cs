namespace Drover.Server.Runs;

/// <summary>
/// Runs of a space, and the approvals they wait on. Reading a run runs under the tenant scope filter. Cancelling one
/// and answering an approval open their own scope in the service, because both tell the workers after the commit.
/// </summary>
public static class RunEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapRunEndpoints(this RouteGroupBuilder space)
    {
        space.MapGroup("/runs").AddEndpointFilter<TenantScopeFilter>()
            .MapGet("/{runId:guid}", GetRun).WithName("GetRun");
        space.MapPost("/runs/{runId:guid}/cancel", CancelRun)
            .WithName("CancelRun")
            .WithSummary("Asks the run and the runs below it to stop. The run's own person or a space admin may.");
        space.MapPost("/tool-calls/{toolCallId:guid}/decision", DecideToolCall)
            .WithName("DecideToolCall")
            .WithSummary("Approves or denies a tool call that waits for a person. The run's own person or a space admin may, if they can see the call.");
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

        return decision.Outcome switch
        {
            DecisionOutcome.NotFound => TypedResults.NotFound(),
            DecisionOutcome.Forbidden => TypedResults.Forbid(),
            DecisionOutcome.NotWaiting => TypedResults.Problem(title: "This call is not waiting for an answer any more.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(decision.Run!.ToSummary()),
        };
    }

    #endregion
}
