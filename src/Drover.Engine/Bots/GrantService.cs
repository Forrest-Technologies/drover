namespace Drover.Engine.Bots;

/// <summary>
/// One tool as a bot's editor shows it. Own is the bot's own setting, null when it holds no grant. The ceiling is the
/// most the bot may be given: what its parent can do, or allow for a bot with no parent. A tool that always asks waits
/// for a person before every use, whatever the grant.
/// </summary>
public sealed record BotToolView(
    Tool Tool,
    ToolConnection? Connection,
    GrantEffect? Own,
    bool NeedsReview,
    GrantEffect? Ceiling,
    string? CeilingBotName,
    bool AlwaysAsks = false);

/// <summary>The result of setting a grant. With neither a tool nor a problem, there is no such bot or tool.</summary>
public sealed record GrantChange(BotToolView? Tool, string? Problem)
{
    public static GrantChange NotFound { get; } = new(null, null);
}

/// <summary>One tool's wanted setting. A null effect is off: the bot's grant is taken away.</summary>
public sealed record WantedGrant(Guid ToolId, GrantEffect? Effect);

/// <summary>The result of setting several grants at once. With neither tools nor a problem, there is no such bot or tool.</summary>
public sealed record GrantsChange(IReadOnlyList<BotToolView>? Tools, string? Problem)
{
    public static GrantsChange NotFound { get; } = new(null, null);
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

        return [.. (await ListAllTools(cancellationToken)).Select(tool => View(tool, byId, own.GetValueOrDefault(tool.Id), parent, ceilings) with
        {
            AlwaysAsks = catalog.AlwaysAsks(tool),
        })];
    }

    /// <summary>Sets one tool for one bot. A null effect is off: the bot's grant is taken away.</summary>
    public async Task<GrantChange> SetGrant(Guid botId, Guid toolId, GrantEffect? effect, Guid actorUserId, CancellationToken cancellationToken)
    {
        var change = await SetGrants(botId, [new(toolId, effect)], actorUserId, cancellationToken);
        return new GrantChange(change.Tools?.Single(), change.Problem);
    }

    /// <summary>
    /// Sets several tools for one bot together. Every change is checked against the parent before any is made, so
    /// either all of them hold or none does. A null effect is off.
    /// </summary>
    public async Task<GrantsChange> SetGrants(Guid botId, IReadOnlyList<WantedGrant> wanted, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(botId, cancellationToken) is not { } bot)
        {
            return GrantsChange.NotFound;
        }

        var all = (await ListAllTools(cancellationToken)).ToDictionary(tool => tool.Id);

        if (wanted.Any(change => !all.ContainsKey(change.ToolId)))
        {
            return GrantsChange.NotFound;
        }

        var parent = await FindParent(bot, cancellationToken);
        var ceilings = parent is null ? null : await tools.ListEffectiveGrants(parent.Id, cancellationToken);

        if (parent is not null && FindCeilingProblem(wanted, ceilings!, parent) is { } problem)
        {
            return new GrantsChange(null, problem);
        }

        var own = (await tools.ListOwnGrants(botId, cancellationToken)).ToDictionary(grant => grant.ToolId);
        var byId = (await connections.ListConnections(cancellationToken)).ToDictionary(connection => connection.Id);
        List<BotToolView> changed = [];

        foreach (var change in wanted)
        {
            var tool = all[change.ToolId];
            var stored = await Write(bot, tool, own.GetValueOrDefault(tool.Id), change.Effect, actorUserId, cancellationToken);
            changed.Add(View(tool, byId, stored, parent, ceilings) with { AlwaysAsks = catalog.AlwaysAsks(tool) });
        }

        return new GrantsChange(changed, null);
    }

    /// <summary>A grant as the API and the audit log spell it. No grant, and an explicit deny, are both off.</summary>
    public static string Word(GrantEffect? effect) => effect switch
    {
        GrantEffect.Allow => "allow",
        GrantEffect.Ask => "ask",
        _ => "off",
    };

    /// <summary>
    /// A grant as the audit log keeps it: the stored effect and review state, so that a deny and no grant, which the
    /// editor both shows as off, are told apart. No grant is both values absent.
    /// </summary>
    public static JsonObject Snapshot(ToolGrant? grant) => new()
    {
        ["effect"] = grant is null ? null : Words.Of(grant.Effect),
        ["review_state"] = grant is null ? null : Words.Of(grant.ReviewState),
    };

    #endregion

    #region Private Methods

    /// <summary>
    /// A first grant on a tool whose changed definition nobody has accepted waits for that review like every other
    /// grant on it. Without this, taking a grant away and giving it back would be a way around the review.
    /// </summary>
    private async Task<ReviewState> ReviewStateForNewGrant(Tool tool, CancellationToken cancellationToken) =>
        tool.ReviewedDefinition is not null || (await connections.ListToolsNeedingReview([tool.Id], cancellationToken)).Count > 0
            ? ReviewState.NeedsReview
            : ReviewState.Current;

    // Writes one grant as it is wanted and records the change. Returns the grant as it now stands, null for off.
    private async Task<ToolGrant?> Write(Bot bot, Tool tool, ToolGrant? stored, GrantEffect? effect, Guid actorUserId, CancellationToken cancellationToken)
    {
        var before = Snapshot(stored);

        if (effect is null && stored is not null)
        {
            tools.Remove(stored);
            stored = null;
        }
        else if (effect is { } set && stored is null)
        {
            stored = new ToolGrant
            {
                BotId = bot.Id,
                ToolId = tool.Id,
                Effect = set,
                ReviewState = await ReviewStateForNewGrant(tool, cancellationToken),
                GrantedByUserId = actorUserId,
            };
            tools.Add(stored);
        }
        else if (effect is { } changed && stored is not null && stored.Effect != changed)
        {
            // The review state is never touched here: choosing an effect is not reading a changed definition.
            stored.Effect = changed;
            stored.GrantedByUserId = actorUserId;
            stored.GrantedByBotId = null;
        }

        var about = new JsonObject { ["tool_id"] = tool.Id, ["tool"] = tool.Name };

        if (audit.Record(actorUserId, "grant.changed", AuditTargets.Bot, bot.Id, before, Snapshot(stored), about))
        {
            logger.LogInformation("Bot {BotId} has tool {ToolId} set to {Grant}", bot.Id, tool.Id, Word(effect));
        }

        return stored;
    }

    // The first change that would give the bot more than its parent holds, said as the person is told it.
    private static string? FindCeilingProblem(IReadOnlyList<WantedGrant> wanted, Dictionary<Guid, GrantEffect> ceilings, Bot parent) =>
        wanted
            .Where(change => change.Effect is not null)
            .Select(change => FindCeilingProblem(change.Effect!.Value, ceilings.TryGetValue(change.ToolId, out var ceiling) ? ceiling : null, parent))
            .FirstOrDefault(problem => problem is not null);

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
