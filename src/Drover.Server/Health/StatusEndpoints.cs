namespace Drover.Server.Health;

public static class StatusEndpoints
{
    #region Private Fields

    private static readonly string[] order = ["database", "migrations", "background_work", "key_ring", "public_address"];

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapStatusEndpoints(this RouteGroupBuilder space)
    {
        space.MapGet("/status", GetStatus)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithName("GetSpaceStatus")
            .WithSummary("The state of the server, where the deployment shows it to a space's admins.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<SpaceStatus>> GetStatus(
        HttpContext context,
        HealthCheckService checks,
        PolicyService policy,
        TimeProvider time)
    {
        if (!policy.Deployment.AllowServerStatusInSpaces)
        {
            return TypedResults.Ok(new SpaceStatus(time.GetUtcNow(), null));
        }

        var report = await checks.CheckHealthAsync(check => check.Tags.Contains(StatusChecks.Status), context.RequestAborted);
        return TypedResults.Ok(new SpaceStatus(
            time.GetUtcNow(),
            [.. order.Where(report.Entries.ContainsKey).Select(name => StatusChecks.ToRow(name, report.Entries[name]))]));
    }

    #endregion
}
