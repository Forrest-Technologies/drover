namespace Drover.Engine.Bots;

/// <summary>The authority and ownership checks for editing routines and rules from a bot's run.</summary>
internal sealed class BotAutomation(
    BotRepository bots,
    BotScheduleRepository scheduleRows,
    AccountRepository accounts,
    TenantContext tenant,
    ScheduleService schedules,
    RuleService rules)
{
    #region Public Methods

    public async Task<Bot> Target(Guid caller, Guid target, bool writes, CancellationToken cancellationToken)
    {
        if (tenant is not { SpaceId: { } spaceId, UserId: { } userId }
            || await accounts.FindRole(spaceId, userId, cancellationToken) is not (MembershipRole.Owner or MembershipRole.Admin))
        {
            throw new BuiltinToolException("Drover lets bots manage routines and rules for a space owner or admin.");
        }

        if (writes)
        {
            await bots.LockTree(cancellationToken);
        }

        if (await bots.FindBot(caller, cancellationToken) is not { Status: BotStatus.Active }
            || (caller != target && !await bots.IsBeneath(target, caller, cancellationToken))
            || await bots.FindBot(target, cancellationToken) is not { Status: not BotStatus.Removed } found)
        {
            throw new BuiltinToolException("Choose yourself or a bot beneath you.");
        }

        return found;
    }

    public async Task<object> ListRoutines(Bot target, Guid userId, CancellationToken cancellationToken) =>
        new
        {
            routines = (await scheduleRows.ListSchedules(target.Id, cancellationToken))
                .Where(schedule => schedule.OwnerUserId == userId).Select(Routine).ToList(),
            bots_beneath = await Descendants(target.Id, cancellationToken),
        };

    public async Task<object> ManageRoutine(
        Guid botId, string operation, Guid id, ScheduleEdit? edit, AuditActor actor, CancellationToken cancellationToken)
    {
        if (operation == "remove")
        {
            return await schedules.Remove(botId, id, actor, cancellationToken)
                ? new { removed = id }
                : throw new BuiltinToolException("That routine does not belong to this bot and the person you are working for.");
        }

        var change = operation == "add"
            ? await schedules.Create(botId, edit!, actor, cancellationToken)
            : await schedules.Update(botId, id, edit!, actor, cancellationToken);
        return change.Schedule is { } schedule
            ? Routine(schedule)
            : throw new BuiltinToolException(change.Problem ?? "That routine does not belong to this bot and the person you are working for.");
    }

    public async Task<object> ListRules(Bot target, AuditActor actor, CancellationToken cancellationToken)
    {
        List<object> found = [];

        foreach (var rule in await rules.ListRules(target.Id, cancellationToken) ?? [])
        {
            found.Add(new
            {
                rule = rule.ToResponse(),
                may_change = rule.Distance == 0 && await rules.MayChange(rule.Rule, actor, cancellationToken),
            });
        }

        return new { rules = found, bots_beneath = await Descendants(target.Id, cancellationToken) };
    }

    public async Task<object> ManageRule(
        Guid botId, string operation, Guid id, BotRuleBody? edit, AuditActor actor, CancellationToken cancellationToken)
    {
        if (operation == "remove")
        {
            return await rules.Remove(botId, id, actor, cancellationToken)
                ? new { removed = id }
                : throw new BuiltinToolException("That rule cannot be removed by this bot.");
        }

        var change = operation == "add"
            ? await rules.Create(botId, edit!, actor, cancellationToken)
            : await rules.Update(botId, id, edit!, actor, cancellationToken);
        return change.Rule?.ToResponse()
            ?? throw new BuiltinToolException(change.Problem ?? "That rule cannot be changed by this bot.");
    }

    #endregion

    #region Private Methods

    private async Task<IReadOnlyList<object>> Descendants(Guid botId, CancellationToken cancellationToken) =>
        [.. (await bots.ListDescendants(botId, cancellationToken)).Select(bot => new { bot.Id, bot.Name, bot.Handle })];

    private static object Routine(BotSchedule schedule) => new
    {
        schedule.Id,
        schedule.BotId,
        schedule.Cron,
        schedule.TimeZone,
        schedule.Prompt,
        ThreadMode = Words.Of(schedule.ThreadMode),
        schedule.Enabled,
        schedule.NextRunAt,
    };

    #endregion
}
