namespace Drover.Server.Runs;

/// <summary>
/// What bots and runs used. Owners and admins read the whole space and a member only the runs that act for them; a
/// run in a conversation the caller cannot open is never shown on its own. Every read runs under the tenant scope
/// filter. <c>period</c> is one of <see cref="UsagePeriods"/> and <c>zone</c> the caller's IANA time zone, which
/// decides where "today" starts.
/// </summary>
public static class UsageEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapUsageEndpoints(this RouteGroupBuilder space)
    {
        var scoped = space.MapGroup(string.Empty).AddEndpointFilter<TenantScopeFilter>();
        scoped.MapGet("/usage", GetCrewUsage)
            .WithName("GetCrewUsage")
            .WithSummary("What each bot used in a period, alone and with the bots beneath it.");
        scoped.MapGet("/usage/bots/{botId:guid}/runs", ListCostliestRuns)
            .WithName("ListCostliestRuns")
            .WithSummary("A bot's most expensive runs in a period, and a total for the runs in conversations the caller cannot open.");
        scoped.MapGet("/runs/{runId:guid}/usage", GetRunUsage)
            .WithName("GetRunUsage")
            .WithSummary("What one run used, and with the runs it handed work to.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<CrewUsage>, ProblemHttpResult>> GetCrewUsage(
        string? period,
        string? zone,
        HttpContext context,
        UsageService usage)
    {
        if (Problem(period, zone, out var timeZone) is { } problem)
        {
            return problem;
        }

        var access = context.GetSpaceAccess();
        return TypedResults.Ok(await usage.GetCrewUsage(period!, timeZone!, access.UserId, IsAdmin(access), context.RequestAborted));
    }

    private static async Task<Results<Ok<BotRunsUsage>, ProblemHttpResult>> ListCostliestRuns(
        Guid botId,
        string? period,
        string? zone,
        HttpContext context,
        UsageService usage)
    {
        if (Problem(period, zone, out var timeZone) is { } problem)
        {
            return problem;
        }

        var access = context.GetSpaceAccess();
        return TypedResults.Ok(await usage.ListCostliestRuns(botId, period!, timeZone!, access.UserId, IsAdmin(access), context.RequestAborted));
    }

    private static async Task<Results<Ok<RunUsageDetail>, NotFound>> GetRunUsage(Guid runId, HttpContext context, UsageService usage)
    {
        var access = context.GetSpaceAccess();
        return await usage.GetRunUsage(runId, access.UserId, IsAdmin(access), context.RequestAborted) is { } detail
            ? TypedResults.Ok(detail)
            : TypedResults.NotFound();
    }

    #endregion

    #region Private Methods

    private static bool IsAdmin(SpaceAccess access) => access.Role <= MembershipRole.Admin;

    private static ProblemHttpResult? Problem(string? period, string? zone, out TimeZoneInfo? timeZone)
    {
        timeZone = null;

        if (period is null || !UsagePeriods.All.Contains(period))
        {
            return TypedResults.Problem(title: "Choose today, 7d, or 30d as the period.", statusCode: StatusCodes.Status400BadRequest);
        }

        return zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out timeZone)
            ? null
            : TypedResults.Problem(title: "Name the time zone the period is counted in, such as Europe/Berlin.", statusCode: StatusCodes.Status400BadRequest);
    }

    #endregion
}
