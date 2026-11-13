namespace Drover.Engine.Bots;

/// <summary>
/// One tool as a bot's editor shows it. Own is the bot's own setting, null when it holds no grant. The ceiling is the
/// most the bot may be given: what its parent can do, or allow for a bot with no parent.
/// </summary>
public sealed record BotToolView(Tool Tool, ToolConnection? Connection, GrantEffect? Own, bool NeedsReview, GrantEffect? Ceiling, string? CeilingBotName);

/// <summary>The result of setting a grant. With neither a tool nor a problem, there is no such bot or tool.</summary>
public sealed record GrantChange(BotToolView? Tool, string? Problem)
{
    public static GrantChange NotFound { get; } = new(null, null);
}

/// <summary>
/// What a person sets a bot's tools to. A bot is never given more than its parent can do, and that is checked when
/// the grant is written, through the database's effective-grant function, so the editor cannot be talked past it.
/// </summary>
public sealed class GrantService(
    BotRepository bots,
    ToolRepository tools,
    ToolConnectionRepository connections,
    ToolCatalog catalog,
    AuditLog audit,
    ILogger<GrantService> logger)
{
    #region Public Methods

    /// <summary>Every tool of the space with this bot's setting for it, or null when there is no such bot.</summary>
    public async Task<IReadOnlyList<BotToolView>?> ListTools(Guid botId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { Status: not BotStatus.Removed } bot)
        {
            return null;
        }

        var parent = await FindParent(bot, cancellationToken);
        var ceilings = parent is null ? null : await tools.ListEffectiveGrants(parent.Id, cancellationToken);
        var own = (await tools.ListOwnGrants(botId, cancellationToken)).ToDictionary(grant => grant.ToolId);
        var byId = (await connections.ListConnections(cancellationToken)).ToDictionary(connection => connection.Id);

        return [.. (await ListAllTools(cancellationToken)).Select(tool => View(tool, byId, own.GetValueOrDefault(tool.Id), parent, ceilings))];
    }

    /// <summary>Sets one tool for one bot. A null effect is off: the bot's grant is taken away.</summary>
    public async Task<GrantChange> SetGrant(Guid botId, Guid toolId, GrantEffect? effect, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(botId, cancellationToken) is not { } bot
            || (await ListAllTools(cancellationToken)).Find(candidate => candidate.Id == toolId) is not { } tool)
        {
            return GrantChange.NotFound;
        }

        var parent = await FindParent(bot, cancellationToken);
        var ceiling = parent is null ? GrantEffect.Allow : await tools.FindEffectiveGrant(parent.Id, toolId, cancellationToken);

        if (effect is { } wanted && parent is not null && FindCeilingProblem(wanted, ceiling, parent) is { } problem)
        {
            return new GrantChange(null, problem);
        }

        var stored = (await tools.ListOwnGrants(botId, cancellationToken)).Find(grant => grant.ToolId == toolId);
        var before = Word(stored?.Effect);

        if (effect is null && stored is not null)
        {
            tools.Remove(stored);
            stored = null;
        }
        else if (effect is { } set && stored is null)
        {
            stored = new ToolGrant { BotId = botId, ToolId = toolId, Effect = set, GrantedByUserId = actorUserId };
            tools.Add(stored);
        }
        else if (effect is { } changed && stored is not null)
        {
            // A grant that waits for review keeps waiting: changing its effect is not reading the changed definition.
            stored.Effect = changed;
            stored.GrantedByUserId = actorUserId;
            stored.GrantedByBotId = null;
        }

        var about = new JsonObject { ["tool_id"] = tool.Id, ["tool"] = tool.Name };

        if (audit.Record(actorUserId, "grant.changed", AuditTargets.Bot, bot.Id, new JsonObject { ["grant"] = before }, new JsonObject { ["grant"] = Word(effect) }, about))
        {
            logger.LogInformation("Bot {BotId} has tool {ToolId} set to {Grant}", bot.Id, tool.Id, Word(effect));
        }

        var connection = tool.ToolConnectionId is { } connectionId ? await connections.FindConnection(connectionId, cancellationToken) : null;
        return new GrantChange(new BotToolView(tool, connection, stored?.Effect, stored?.ReviewState is ReviewState.NeedsReview, ceiling, parent?.Name), null);
    }

    /// <summary>A grant as the API and the audit log spell it. No grant, and an explicit deny, are both off.</summary>
    public static string Word(GrantEffect? effect) => effect switch
    {
        GrantEffect.Allow => "allow",
        GrantEffect.Ask => "ask",
        _ => "off",
    };

    #endregion

    #region Private Methods

    private static string? FindCeilingProblem(GrantEffect wanted, GrantEffect? ceiling, Bot parent)
    {
        if (ceiling is null or GrantEffect.Deny)
        {
            return $"{parent.Name} does not have this tool. Give it to {parent.Name} first.";
        }

        return wanted > ceiling
            ? $"{parent.Name} has this tool set to ask. Raise it on {parent.Name} first."
            : null;
    }

    private static BotToolView View(
        Tool tool,
        Dictionary<Guid, ToolConnection> connections,
        ToolGrant? own,
        Bot? parent,
        Dictionary<Guid, GrantEffect>? ceilings) =>
        new(
            tool,
            tool.ToolConnectionId is { } connectionId ? connections.GetValueOrDefault(connectionId) : null,
            own?.Effect,
            own?.ReviewState is ReviewState.NeedsReview,
            ceilings is null ? GrantEffect.Allow : ceilings.TryGetValue(tool.Id, out var ceiling) ? ceiling : null,
            parent?.Name);

    private async Task<Bot?> FindParent(Bot bot, CancellationToken cancellationToken) =>
        bot.ParentBotId is { } parentId ? await bots.FindBot(parentId, cancellationToken) : null;

    /// <summary>Built-in tools first, then the tools of connections. Built-ins a space lacks are added on the way.</summary>
    private async Task<List<Tool>> ListAllTools(CancellationToken cancellationToken) =>
        [
            .. (await catalog.EnsureBuiltins(cancellationToken)).OrderBy(tool => tool.Name, StringComparer.Ordinal),
            .. await tools.ListConnectionTools(cancellationToken),
        ];

    #endregion
}
