namespace Drover.Server.Bots;

/// <summary>
/// The bot editor's API: a bot's whole definition, its tools, and its schedules. Every route here is for the
/// space's owners and admins only, and every change is checked and audited by the service it calls.
/// </summary>
public static class BotEditorEndpoints
{
    #region Public Methods

    public static RouteGroupBuilder MapBotEditorEndpoints(this RouteGroupBuilder bots)
    {
        var bot = bots.MapGroup("/{botId:guid}").RequireSpaceRole(MembershipRole.Admin);
        bot.MapGet("/definition", GetDefinition).WithName("GetBotDefinition");
        bot.MapPut("/definition", UpdateDefinition).WithName("UpdateBotDefinition");
        bot.MapPost("/duplicate", Duplicate).WithName("DuplicateBot");
        bot.MapPost("/pause", Pause).WithName("PauseBot");
        bot.MapPost("/resume", Resume).WithName("ResumeBot");
        bot.MapDelete("/", Remove).WithName("RemoveBot");
        bot.MapGet("/tools", ListTools).WithName("ListBotTools");
        bot.MapPut("/tools/{toolId:guid}", SetGrant).WithName("SetBotToolGrant");
        bot.MapGet("/schedules", ListSchedules).WithName("ListBotSchedules");
        bot.MapPost("/schedules", CreateSchedule).WithName("CreateBotSchedule");
        bot.MapPut("/schedules/{scheduleId:guid}", UpdateSchedule).WithName("UpdateBotSchedule");
        bot.MapDelete("/schedules/{scheduleId:guid}", RemoveSchedule).WithName("RemoveBotSchedule");
        return bots;
    }

    #endregion

    #region Definition

    private static async Task<Results<Ok<BotDetail>, NotFound>> GetDefinition(Guid botId, HttpContext context, BotService bots) =>
        await bots.FindBotView(botId, context.RequestAborted) is { } view
            ? TypedResults.Ok(view.ToDetail())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<BotDetail>, NotFound, ProblemHttpResult>> UpdateDefinition(
        Guid botId,
        UpdateBotRequest request,
        HttpContext context,
        BotService bots)
    {
        if (await bots.FindBotView(botId, context.RequestAborted) is not { } current)
        {
            return TypedResults.NotFound();
        }

        var change = await bots.Update(botId, request.ToEdit(current), context.GetSpaceAccess().UserId, context.RequestAborted);
        return Answer(change);
    }

    private static async Task<Results<Created<BotDetail>, NotFound>> Duplicate(Guid botId, HttpContext context, BotService bots)
    {
        var access = context.GetSpaceAccess();
        var change = await bots.Duplicate(botId, access.UserId, context.RequestAborted);
        return change.View is { } copy
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{copy.Bot.Id}/definition", copy.ToDetail())
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotDetail>, NotFound, ProblemHttpResult>> Pause(Guid botId, HttpContext context, BotService bots) =>
        Answer(await bots.Pause(botId, context.GetSpaceAccess().UserId, context.RequestAborted));

    private static async Task<Results<Ok<BotDetail>, NotFound, ProblemHttpResult>> Resume(Guid botId, HttpContext context, BotService bots) =>
        Answer(await bots.Resume(botId, context.GetSpaceAccess().UserId, context.RequestAborted));

    private static async Task<Results<NoContent, NotFound>> Remove(Guid botId, HttpContext context, BotService bots) =>
        (await bots.Remove(botId, context.GetSpaceAccess().UserId, context.RequestAborted)).Bot is null
            ? TypedResults.NotFound()
            : TypedResults.NoContent();

    #endregion

    #region Tools

    private static async Task<Results<Ok<List<BotToolEntry>>, NotFound>> ListTools(Guid botId, HttpContext context, GrantService grants) =>
        await grants.ListTools(botId, context.RequestAborted) is { } tools
            ? TypedResults.Ok(tools.Select(tool => tool.ToEntry()).ToList())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<BotToolEntry>, NotFound, ProblemHttpResult>> SetGrant(
        Guid botId,
        Guid toolId,
        SetToolGrantRequest request,
        HttpContext context,
        GrantService grants)
    {
        GrantEffect? effect = request.Grant switch
        {
            "allow" => GrantEffect.Allow,
            "ask" => GrantEffect.Ask,
            _ => null,
        };

        if (effect is null && request.Grant is not "off")
        {
            return TypedResults.Problem(title: "Set a tool to off, ask, or allow.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await grants.SetGrant(botId, toolId, effect, context.GetSpaceAccess().UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status409Conflict);
        }

        return change.Tool is { } tool ? TypedResults.Ok(tool.ToEntry()) : TypedResults.NotFound();
    }

    #endregion

    #region Schedules

    private static async Task<Results<Ok<List<BotScheduleResponse>>, NotFound>> ListSchedules(Guid botId, HttpContext context, ScheduleService schedules) =>
        await schedules.ListSchedules(botId, context.RequestAborted) is { } found
            ? TypedResults.Ok(found.Select(schedule => schedule.ToResponse()).ToList())
            : TypedResults.NotFound();

    private static async Task<Results<Created<BotScheduleResponse>, NotFound, ProblemHttpResult>> CreateSchedule(
        Guid botId,
        BotScheduleBody body,
        HttpContext context,
        ScheduleService schedules)
    {
        var access = context.GetSpaceAccess();
        var change = await schedules.Create(botId, body.ToEdit(), access.UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Schedule is { } schedule
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{botId}/schedules/{schedule.Id}", schedule.ToResponse())
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotScheduleResponse>, NotFound, ProblemHttpResult>> UpdateSchedule(
        Guid botId,
        Guid scheduleId,
        BotScheduleBody body,
        HttpContext context,
        ScheduleService schedules)
    {
        var change = await schedules.Update(botId, scheduleId, body.ToEdit(), context.GetSpaceAccess().UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Schedule is { } schedule ? TypedResults.Ok(schedule.ToResponse()) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveSchedule(Guid botId, Guid scheduleId, HttpContext context, ScheduleService schedules) =>
        await schedules.Remove(botId, scheduleId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    #endregion

    #region Private Methods

    private static Results<Ok<BotDetail>, NotFound, ProblemHttpResult> Answer(BotChange change)
    {
        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.View is { } view ? TypedResults.Ok(view.ToDetail()) : TypedResults.NotFound();
    }

    #endregion
}
