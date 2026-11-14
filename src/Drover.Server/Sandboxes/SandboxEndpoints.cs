namespace Drover.Server.Sandboxes;

/// <summary>
/// A person's controls for a bot's own sandbox: its state, pause, resume, rebuild, turn off, its screen, the mouse and
/// keyboard, and a terminal. For the space's owners and admins, the people who may change the bot. These wait on the
/// sandbox provider, so they sit outside the tenant scope filter: the service opens its own short transactions and
/// holds none across a provider call. Every state-changing call is a POST and carries the antiforgery token, the
/// screen included, because looking at a paused sandbox resumes it. No answer names the provider or carries its ids
/// or tokens.
/// </summary>
public static class SandboxEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapSandboxEndpoints(this RouteGroupBuilder space)
    {
        var sandbox = space.MapGroup("/bots/{botId:guid}/sandbox")
            .RequireSpaceRole(MembershipRole.Admin)
            .WithBodyLimit(MaxBodyBytes);
        sandbox.MapGet("/", GetStatus).WithName("GetBotSandbox");
        sandbox.MapPost("/pause", Pause).WithName("PauseBotSandbox");
        sandbox.MapPost("/resume", Resume).WithName("ResumeBotSandbox");
        sandbox.MapPost("/rebuild", Rebuild).WithName("RebuildBotSandbox")
            .WithSummary("Replaces the sandbox with a fresh one. Its files are gone.");
        sandbox.MapPost("/turn-off", TurnOff).WithName("TurnOffBotSandbox")
            .WithSummary("Destroys the sandbox now. Its files are gone; the next use makes a new one.");
        sandbox.MapPost("/screen", Screen).WithName("GetBotSandboxScreen")
            .WithSummary("The sandbox's screen as a PNG, never stored. A paused sandbox is resumed and a missing one made.");
        sandbox.MapPost("/input", Input).WithName("SendBotSandboxInput");
        sandbox.MapPost("/commands", RunCommand).WithName("RunBotSandboxCommand");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<BotSandboxStatus>, NotFound, ProblemHttpResult>> GetStatus(
        Guid botId,
        HttpContext context,
        SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.GetStatus(access.SpaceId, access.UserId, botId, context.RequestAborted), TypedResults.Ok);
    }

    private static async Task<Results<Ok<BotSandboxStatus>, NotFound, ProblemHttpResult>> Pause(Guid botId, HttpContext context, SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.Pause(access.SpaceId, access.UserId, botId, context.RequestAborted), TypedResults.Ok);
    }

    private static async Task<Results<Ok<BotSandboxStatus>, NotFound, ProblemHttpResult>> Resume(Guid botId, HttpContext context, SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.Resume(access.SpaceId, access.UserId, botId, context.RequestAborted), TypedResults.Ok);
    }

    private static async Task<Results<Ok<BotSandboxStatus>, NotFound, ProblemHttpResult>> Rebuild(Guid botId, HttpContext context, SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.Rebuild(access.SpaceId, access.UserId, botId, context.RequestAborted), TypedResults.Ok);
    }

    private static async Task<Results<Ok<BotSandboxStatus>, NotFound, ProblemHttpResult>> TurnOff(Guid botId, HttpContext context, SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.TurnOff(access.SpaceId, access.UserId, botId, context.RequestAborted), TypedResults.Ok);
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ProblemHttpResult>> Screen(Guid botId, HttpContext context, SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        var shot = await sandboxes.Screen(access.SpaceId, access.UserId, botId, context.RequestAborted);

        // A picture of somebody's screen: no cache anywhere keeps it.
        context.Response.Headers.CacheControl = "no-store";
        return Answer(shot, picture => TypedResults.File(picture.Png, "image/png"));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Input(
        Guid botId,
        SandboxInputRequest input,
        HttpContext context,
        SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.Input(access.SpaceId, access.UserId, botId, input, context.RequestAborted), _ => TypedResults.NoContent());
    }

    private static async Task<Results<Ok<SandboxCommandResponse>, NotFound, ProblemHttpResult>> RunCommand(
        Guid botId,
        SandboxCommandRequest request,
        HttpContext context,
        SandboxControlService sandboxes)
    {
        var access = context.GetSpaceAccess();
        return Answer(await sandboxes.Run(access.SpaceId, access.UserId, botId, request.Command ?? string.Empty, context.RequestAborted), TypedResults.Ok);
    }

    #endregion

    #region Private Methods

    private static Results<TOk, NotFound, ProblemHttpResult> Answer<T, TOk>(SandboxControlResult<T> result, Func<T, TOk> ok)
        where TOk : IResult =>
        result.Refusal switch
        {
            SandboxControlRefusal.None => ok(result.Value!),
            SandboxControlRefusal.NotFound => TypedResults.NotFound(),
            SandboxControlRefusal.NotPermitted => TypedResults.Problem(title: result.Problem, statusCode: StatusCodes.Status403Forbidden),
            SandboxControlRefusal.Invalid => TypedResults.Problem(title: result.Problem, statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Problem(title: result.Problem, statusCode: StatusCodes.Status409Conflict),
        };

    #endregion

    #region Constants

    /// <summary>The largest body: a command of at most 4000 characters, every one escaped, with room to spare.</summary>
    private const long MaxBodyBytes = 64 * 1024;

    #endregion
}
