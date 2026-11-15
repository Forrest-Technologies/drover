namespace Drover.Engine.Bots;

/// <summary>A bot to add to a crew, as a crew tool's call asks for it. Null <see cref="ParentBotId"/> is the calling bot.</summary>
public sealed record CrewAddition(string Name, string Description, string Instructions, Guid? ParentBotId);

/// <summary>What a crew tool's call changes on a bot. A null part is left as it is.</summary>
public sealed record CrewChange(string? Name, string? Description, string? Instructions, BotStatus? Status);


/// <summary>
/// What a bot may do to the bots beneath it, through the crew tools. Every check is made here, on the server, from
/// the run's own bot and the run's person, never from anything the model wrote: a bot acts only on bots strictly
/// beneath it, only where this server lets bots build bots, and only with the authority of its run's person, who
/// must be able to do the same through the API. Each change goes through <see cref="BotService"/>, the path a
/// person's change takes, so the same caps, limits, starting grants, and audit entries apply; the entry names the
/// bot as the actor and the person it acted for. A refusal is a <see cref="BuiltinToolException"/> in plain words,
/// thrown inside the caller's tenant scope, so nothing of a refused call is saved.
/// </summary>
public sealed class CrewService(
    BotRepository bots,
    BotService botService,
    AccountRepository accounts,
    TenantContext tenant,
    PolicyService policies,
    ILogger<CrewService> logger)
{
    #region Public Methods

    /// <summary>Every bot beneath the calling bot that has not been removed, nearest first.</summary>
    public async Task<List<Bot>> List(Guid botId, CancellationToken cancellationToken)
    {
        await CheckCaller(botId, changesBots: false, cancellationToken);
        return await bots.ListDescendants(botId, cancellationToken);
    }

    /// <summary>
    /// Makes a bot beneath the calling bot, or beneath a bot already beneath it, on the calling bot's own model
    /// connection and model. It starts with the tools every new bot beneath a parent starts with, no higher than its
    /// parent holds them, and never with the crew tools.
    /// </summary>
    public async Task<Bot> Add(Guid botId, CrewAddition addition, CancellationToken cancellationToken)
    {
        var (caller, actor) = await CheckCaller(botId, changesBots: true, cancellationToken);
        var parentId = addition.ParentBotId ?? botId;

        if (parentId != botId)
        {
            await RequireBeneath(botId, parentId, cancellationToken);
        }

        var request = new NewBot(addition.Name, addition.Description, addition.Instructions, caller.ModelConnectionId, caller.Model, parentId);
        var made = await botService.Create(request, actor, cancellationToken);

        if (made.Bot is not { } bot)
        {
            throw new BuiltinToolException(made.Problem ?? NotBeneath(parentId));
        }

        logger.LogInformation("Bot {BotId} made bot {NewBotId}", botId, bot.Id);
        return bot;
    }

    /// <summary>Changes the name, what it is for, the instructions, or whether it is paused, of a bot beneath the caller.</summary>
    public async Task<Bot> Change(Guid botId, Guid targetId, CrewChange change, CancellationToken cancellationToken)
    {
        var (_, actor) = await CheckCaller(botId, changesBots: true, cancellationToken);
        await RequireBeneath(botId, targetId, cancellationToken);
        var changed = await botService.FindBot(targetId, cancellationToken) ?? throw new BuiltinToolException(NotBeneath(targetId));

        if (change.Name is not null || change.Description is not null || change.Instructions is not null)
        {
            changed = Accepted(await botService.Update(targetId, await EditOf(targetId, change, cancellationToken), actor, cancellationToken), targetId);
        }

        return change.Status switch
        {
            BotStatus.Paused => Accepted(await botService.Pause(targetId, actor, cancellationToken), targetId),
            BotStatus.Active => Accepted(await botService.Resume(targetId, actor, cancellationToken), targetId),
            _ => changed,
        };
    }

    /// <summary>Removes a bot beneath the caller, as a person's remove does: its sandbox is retired, its history kept.</summary>
    public async Task<Bot> Remove(Guid botId, Guid targetId, CancellationToken cancellationToken)
    {
        var (_, actor) = await CheckCaller(botId, changesBots: true, cancellationToken);
        await RequireBeneath(botId, targetId, cancellationToken);
        var removed = Accepted(await botService.Remove(targetId, actor, cancellationToken), targetId);
        logger.LogInformation("Bot {BotId} removed bot {RemovedBotId}", botId, targetId);
        return removed;
    }

    #endregion

    #region Authority

    /// <summary>
    /// The calling bot and who it acts as, once the server, the run's person, and the bot itself allow it to manage
    /// bots. Reading the bots beneath it needs a member; changing them needs a person who may change bots through
    /// the API, an owner or admin, because a bot never has more authority than the person its run is for.
    /// </summary>
    private async Task<(Bot Caller, AuditActor Actor)> CheckCaller(Guid botId, bool changesBots, CancellationToken cancellationToken)
    {
        if (!policies.Deployment.AllowBotsBuildingBots)
        {
            throw new BuiltinToolException(
                $"This server does not let bots make or manage bots ({nameof(DeploymentPolicy.AllowBotsBuildingBots)}). A person can do it in the bot editor.");
        }

        if (tenant is not { SpaceId: { } spaceId, UserId: { } userId }
            || await accounts.FindRole(spaceId, userId, cancellationToken) is not { } role)
        {
            throw new BuiltinToolException("The person this bot is working for is not a member of this space.");
        }

        if (changesBots && role is not (MembershipRole.Owner or MembershipRole.Admin))
        {
            throw new BuiltinToolException(
                "Only an owner or admin of this space can make or change bots, and the person this bot is working for is neither, so it cannot do it for them.");
        }

        if (await bots.FindBot(botId, cancellationToken) is not { Status: BotStatus.Active } caller)
        {
            throw new BuiltinToolException("This bot is paused or removed, so it cannot manage bots.");
        }

        return (caller, AuditActor.BotFor(botId, userId));
    }

    private async Task RequireBeneath(Guid botId, Guid targetId, CancellationToken cancellationToken)
    {
        if (targetId == botId)
        {
            throw new BuiltinToolException("A bot cannot change itself with the crew tools, only the bots beneath it. A person changes this bot in the bot editor.");
        }

        if (!await bots.IsBeneath(targetId, botId, cancellationToken))
        {
            throw new BuiltinToolException(NotBeneath(targetId));
        }
    }

    private static string NotBeneath(Guid targetId) =>
        $"There is no bot with the id {targetId} beneath this bot. A bot can manage only the bots beneath it; crew_list shows them.";

    private static Bot Accepted(BotChange change, Guid targetId) =>
        change.Bot ?? throw new BuiltinToolException(change.Problem ?? NotBeneath(targetId));

    /// <summary>The bot's whole definition with the parts the call names changed, so the person path checks every part.</summary>
    private async Task<BotEdit> EditOf(Guid targetId, CrewChange change, CancellationToken cancellationToken)
    {
        var current = await botService.FindBotView(targetId, cancellationToken) ?? throw new BuiltinToolException(NotBeneath(targetId));
        var request = new UpdateBotRequest(change.Name, null, null, change.Description, change.Instructions, null, null, null, null, null, null, null, null);
        return request.ToEdit(current);
    }

    #endregion
}
