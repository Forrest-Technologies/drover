namespace Drover.Server.Bots;

/// <summary>Bots of a space, under the tenant scope filter. Any member may see them; an admin creates them.</summary>
public static class BotEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapBotEndpoints(this RouteGroupBuilder space)
    {
        var bots = space.MapGroup("/bots").AddEndpointFilter<TenantScopeFilter>();
        bots.MapGet("/", ListBots).WithName("ListBots");
        bots.MapPost("/", CreateBot).RequireSpaceRole(MembershipRole.Admin).WithName("CreateBot");
        bots.MapGet("/{botId:guid}", GetBot).WithName("GetBot");
        bots.MapGet("/{botId:guid}/threads", ListThreads).WithName("ListThreads");
        bots.MapPost("/{botId:guid}/threads", CreateThread).WithName("CreateThread");
        bots.MapPut("/{botId:guid}/main-thread", OpenMainThread).WithName("OpenMainThread");
        bots.MapBotEditorEndpoints();
        bots.MapMemoryEndpoints();
        space.MapScheduleRunOnce();
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<BotSummary>>> ListBots(HttpContext context, BotService bots) =>
        TypedResults.Ok((await bots.ListBots(context.RequestAborted)).Select(bot => bot.ToSummary()).ToList());

    private static async Task<Results<Created<BotSummary>, ProblemHttpResult>> CreateBot(
        CreateBotRequest request,
        HttpContext context,
        BotService bots)
    {
        var access = context.GetSpaceAccess();
        var change = await bots.Create(
            new NewBot(request.Name ?? string.Empty, string.Empty, request.Instructions ?? string.Empty, request.ModelConnectionId, request.Model, request.ParentBotId),
            AuditActor.Person(access.UserId),
            context.RequestAborted);

        return change.Bot is { } bot
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{bot.Id}", bot.ToSummary())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<BotSummary>, NotFound>> GetBot(Guid botId, HttpContext context, BotService bots) =>
        await bots.FindBot(botId, context.RequestAborted) is { } bot
            ? TypedResults.Ok(bot.ToSummary())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<ThreadSummary>>, NotFound>> ListThreads(
        Guid botId,
        HttpContext context,
        ThreadService threads)
    {
        var found = await threads.ListThreads(botId, context.GetSpaceAccess().UserId, context.RequestAborted);
        return found is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(found.Select(view => view.Thread.ToSummary(view.ActiveRunId, view.Attention)).ToList());
    }

    private static async Task<Results<Created<ThreadSummary>, NotFound>> CreateThread(
        Guid botId,
        CreateThreadRequest? request,
        HttpContext context,
        ThreadService threads)
    {
        var access = context.GetSpaceAccess();
        var thread = await threads.Create(botId, request?.Title, access.UserId, context.RequestAborted, request?.ProjectId);

        return thread is null
            ? TypedResults.NotFound()
            : TypedResults.Created($"/api/spaces/{access.SpaceId}/threads/{thread.Id}", thread.ToSummary(null));
    }

    /// <summary>The caller's main thread with the bot, made on first use. Asking again returns the same thread.</summary>
    private static async Task<Results<Ok<ThreadSummary>, NotFound>> OpenMainThread(
        Guid botId,
        HttpContext context,
        ThreadService threads)
    {
        var main = await threads.OpenMainThread(botId, context.GetSpaceAccess().UserId, context.RequestAborted);
        return main is null ? TypedResults.NotFound() : TypedResults.Ok(main.Thread.ToSummary(main.ActiveRunId, main.Attention));
    }

    #endregion
}
