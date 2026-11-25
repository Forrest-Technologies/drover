namespace Drover.Server.Bots;

/// <summary>
/// The bot editor's API: a bot's whole definition, its tools, and its schedules. Every route here is for the
/// space's owners and admins only, and every change is checked and audited by the service it calls. Running a
/// schedule once starts a run and tells the workers after the commit, so that route sits outside the tenant scope
/// filter and the service opens its own scope.
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
        bot.MapPost("/schedules/check", CheckSchedule)
            .WithName("CheckBotSchedule")
            .WithSummary("Checks a time as saving a schedule would: the sentence it would be refused with, or its next times.");
        bot.MapPut("/schedules/{scheduleId:guid}", UpdateSchedule).WithName("UpdateBotSchedule");
        bot.MapDelete("/schedules/{scheduleId:guid}", RemoveSchedule).WithName("RemoveBotSchedule");
        bot.MapGet("/rules", ListRules).WithName("ListBotRules");
        bot.MapPost("/rules", CreateRule).WithName("CreateBotRule");
        bot.MapPut("/rules/{ruleId:guid}", UpdateRule).WithName("UpdateBotRule");
        bot.MapDelete("/rules/{ruleId:guid}", RemoveRule).WithName("RemoveBotRule");
        bot.MapPost("/rules/try", TryRules).WithName("TryBotRules");
        bot.MapGet("/rules/options", GetRuleOptions).WithName("GetBotRuleOptions");
        bot.MapGet("/sandbox/options", GetSandboxOptions).WithName("GetBotSandboxOptions");
        bot.MapGet("/limits/options", GetRunLimitOptions).WithName("GetBotRunLimitOptions")
            .WithSummary("The space's limits on one run, which a bot's own limits may only lower.");
        return bots;
    }

    public static RouteGroupBuilder MapScheduleRunOnce(this RouteGroupBuilder space)
    {
        space.MapPost("/bots/{botId:guid}/schedules/{scheduleId:guid}/run-once", RunScheduleOnce)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithName("RunBotScheduleOnce")
            .WithSummary("Sends the schedule's prompt once now, as the caller's own message, and returns the conversation. The schedule's next time does not change.");
        return space;
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

        var change = await bots.Update(botId, request.ToEdit(current), AuditActor.Person(context.GetSpaceAccess().UserId), context.RequestAborted);
        return Answer(change);
    }

    private static async Task<Results<Created<BotDetail>, NotFound, ProblemHttpResult>> Duplicate(Guid botId, HttpContext context, BotService bots)
    {
        var access = context.GetSpaceAccess();
        var change = await bots.Duplicate(botId, access.UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.View is { } copy
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{copy.Bot.Id}/definition", copy.ToDetail())
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotDetail>, NotFound, ProblemHttpResult>> Pause(Guid botId, HttpContext context, BotService bots) =>
        Answer(await bots.Pause(botId, AuditActor.Person(context.GetSpaceAccess().UserId), context.RequestAborted));

    private static async Task<Results<Ok<BotDetail>, NotFound, ProblemHttpResult>> Resume(Guid botId, HttpContext context, BotService bots) =>
        Answer(await bots.Resume(botId, AuditActor.Person(context.GetSpaceAccess().UserId), context.RequestAborted));

    /// <summary>
    /// Removes the bot. <c>beneath</c> is "remove" or "keep", and is required when the bot has bots beneath it: without
    /// it the answer is 400 with a title saying a choice is needed, so no client can skip asking the person.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Remove(
        Guid botId,
        string? beneath,
        HttpContext context,
        BotService bots)
    {
        var choice = Words.Read<BotsBeneath>(beneath);

        if (beneath is not null && choice is null)
        {
            return TypedResults.Problem(title: "Choose remove or keep for the bots beneath it.", statusCode: StatusCodes.Status400BadRequest);
        }

        var change = await bots.Remove(botId, choice, AuditActor.Person(context.GetSpaceAccess().UserId), context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Bot is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

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

    private static Ok<ScheduleCheckResponse> CheckSchedule(ScheduleCheckBody body, ScheduleService schedules)
    {
        var check = schedules.Check(body.Cron ?? string.Empty, body.TimeZone ?? string.Empty);
        return TypedResults.Ok(new ScheduleCheckResponse(check.Problem, check.Next));
    }

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

        return change.Schedule is null ? TypedResults.NotFound() : TypedResults.Ok(change.ToResponse());
    }

    private static async Task<Results<NoContent, NotFound>> RemoveSchedule(Guid botId, Guid scheduleId, HttpContext context, ScheduleService schedules) =>
        await schedules.Remove(botId, scheduleId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Results<Accepted<ScheduleRunOnceResponse>, NotFound, ProblemHttpResult>> RunScheduleOnce(
        Guid botId,
        Guid scheduleId,
        HttpContext context,
        ScheduleFiring firing)
    {
        var access = context.GetSpaceAccess();
        var trial = await firing.RunOnce(botId, scheduleId, access.UserId, context.RequestAborted);

        return trial switch
        {
            { Problem: { } problem } => TypedResults.Problem(title: problem, statusCode: StatusCodes.Status409Conflict),
            { ThreadId: { } threadId } => TypedResults.Accepted(
                trial.RunId is { } runId ? $"/api/spaces/{access.SpaceId}/runs/{runId}" : null,
                new ScheduleRunOnceResponse(threadId, trial.RunId)),
            _ => TypedResults.NotFound(),
        };
    }

    #endregion

    #region Sandbox

    // What this server lets a sandbox be. The same for every bot; it sits here so the editor asks with the access it
    // already has.
    private static Ok<BotSandboxOptions> GetSandboxOptions(SandboxAvailability sandboxes, PolicyService policies) =>
        TypedResults.Ok(new BotSandboxOptions(
            sandboxes.IsConfigured,
            policies.Deployment.AllowOpenSandboxNetwork,
            policies.Deployment.MaxSandboxIdleMinutes,
            policies.Deployment.AllowDesktopSandboxes));

    #endregion

    #region Limits

    // What the bot's space lets one run use. The same for every bot of the space; it sits here so the editor asks with
    // the access it already has.
    private static async Task<Ok<BotRunLimitOptions>> GetRunLimitOptions(HttpContext context, PolicyService policies)
    {
        var limits = await policies.GetRuns(context.GetSpaceAccess().SpaceId, context.RequestAborted);
        return TypedResults.Ok(new BotRunLimitOptions(
            limits.MaxSteps,
            limits.MaxTokens,
            limits.MaxDelegations));
    }

    #endregion

    #region Rules

    // What the server's deployment rules leave a rule to use. The same for every bot; it sits here so the editor
    // asks with the access it already has.
    private static Ok<BotRuleOptions> GetRuleOptions(RuleService rules) =>
        TypedResults.Ok(new BotRuleOptions(rules.AllowsWrittenPatterns));

    private static async Task<Results<Ok<List<BotRuleResponse>>, NotFound>> ListRules(Guid botId, HttpContext context, RuleService rules) =>
        await rules.ListRules(botId, context.RequestAborted) is { } found
            ? TypedResults.Ok(await rules.Describe(found, context.RequestAborted))
            : TypedResults.NotFound();

    private static async Task<Results<Created<BotRuleResponse>, NotFound, ProblemHttpResult>> CreateRule(
        Guid botId,
        BotRuleBody body,
        HttpContext context,
        RuleService rules)
    {
        var access = context.GetSpaceAccess();
        var change = await rules.Create(botId, body, access.UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Rule is { } rule
            ? TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{botId}/rules/{rule.Rule.Id}", (await rules.Describe([rule], context.RequestAborted))[0])
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotRuleResponse>, NotFound, ProblemHttpResult>> UpdateRule(
        Guid botId,
        Guid ruleId,
        BotRuleBody body,
        HttpContext context,
        RuleService rules)
    {
        var change = await rules.Update(botId, ruleId, body, context.GetSpaceAccess().UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Rule is { } rule ? TypedResults.Ok((await rules.Describe([rule], context.RequestAborted))[0]) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveRule(Guid botId, Guid ruleId, HttpContext context, RuleService rules) =>
        await rules.Remove(botId, ruleId, context.GetSpaceAccess().UserId, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    /// <summary>What a run would do with a sample. It changes nothing, and it runs the evaluator a run uses.</summary>
    private static async Task<Results<Ok<TryRulesResponse>, NotFound, ProblemHttpResult>> TryRules(
        Guid botId,
        TryRulesRequest request,
        HttpContext context,
        RuleService rules)
    {
        var trial = await rules.Try(botId, request, context.RequestAborted);

        if (trial.BotMissing)
        {
            return TypedResults.NotFound();
        }

        if (trial.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = trial.Outcome;
        return TypedResults.Ok(new TryRulesResponse(
            outcome is null ? null : Words.Of(outcome.Effect),
            outcome?.Rule.Id is { } id && id != Guid.Empty ? id : null,
            outcome?.Rule.Name,
            outcome?.Why,
            outcome?.RedactedText,
            outcome?.RedactedJson?.GetRawText(),
            [.. (outcome?.Applied ?? []).Select(rule => rule.Name)])
        {
            CatchesCommonForms = trial.TriedAPattern,
        });
    }

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
