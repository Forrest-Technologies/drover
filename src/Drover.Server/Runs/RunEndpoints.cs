namespace Drover.Server.Runs;

/// <summary>
/// Runs of a space. Reading one runs under the tenant scope filter. Cancelling opens its own scope in the service,
/// because it tells the workers after the commit.
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
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<RunSummary>, NotFound>> GetRun(Guid runId, HttpContext context, RunService runs) =>
        await runs.FindRun(runId, context.RequestAborted) is { } run
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

    #endregion
}
