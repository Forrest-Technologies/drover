namespace Drover.Engine.Bots;

/// <summary>
/// Bots of the current space: reading them, and every change a person makes to one. Each change is checked here, on
/// the server, and written to the audit log in the same scope. The caller, an endpoint, owns the tenant scope.
/// </summary>
public sealed class BotService(
    BotRepository bots,
    ModelConnectionRepository connections,
    ToolRepository tools,
    ToolCatalog catalog,
    AuditLog audit,
    PolicyService policies,
    SandboxRepository sandboxes,
    TimeProvider time,
    ILogger<BotService> logger)
{
    #region Private Fields

    private const int MaxNameLength = 200;

    private const int MaxHandleLength = 100;

    private const int MaxDescriptionLength = 2_000;

    private const int MaxInstructionsLength = 32_000;

    private const int MaxModelLength = 200;

    private const int MaxSymbolLength = 16;

    private const int MaxAllowedHosts = 50;

    private const int MaxBuildLimit = 1_000;

    private const double MaxTemperature = 2;

    private const int MaxEffortLength = 32;

    private const int MaxAnswerTokens = 1_000_000;

    private const int MaxCompactAtTokens = 10_000_000;

    private const int MaxKeptMessages = 10_000;

    #endregion

    #region Public Methods

    public Task<List<Bot>> ListBots(CancellationToken cancellationToken) => bots.ListBots(cancellationToken);

    public async Task<Bot?> FindBot(Guid id, CancellationToken cancellationToken) =>
        await bots.FindBot(id, cancellationToken) is { Status: not BotStatus.Removed } bot ? bot : null;

    /// <summary>The bot with everything its editor shows, or null when there is no such bot.</summary>
    public async Task<BotView?> FindBotView(Guid id, CancellationToken cancellationToken) =>
        await FindBot(id, cancellationToken) is { } bot
            ? new BotView(bot, await bots.ListSandboxShares(id, cancellationToken))
            : null;

    /// <summary>Creates a bot with no parent and gives it the tools every new bot starts with.</summary>
    public async Task<BotChange> Create(
        string name,
        string instructions,
        Guid modelConnectionId,
        string? model,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return new BotChange(null, $"Give the bot a name of up to {MaxNameLength} characters.");
        }

        if (instructions.Length > MaxInstructionsLength)
        {
            return new BotChange(null, $"Keep the instructions under {MaxInstructionsLength} characters.");
        }

        var connection = await connections.FindConnection(modelConnectionId, cancellationToken);

        if (connection is null)
        {
            return new BotChange(null, "Choose a model connection of this space.");
        }

        var chosen = string.IsNullOrWhiteSpace(model) ? connection.DefaultModel : model.Trim();

        if (string.IsNullOrWhiteSpace(chosen) || chosen.Length > MaxModelLength)
        {
            return new BotChange(null, "Choose a model for the bot.");
        }

        var bot = new Bot
        {
            Name = name.Trim(),
            Handle = await FindFreeHandle(name, cancellationToken),
            Instructions = instructions,
            ModelConnectionId = connection.Id,
            Model = chosen,
            CreatedByUserId = createdByUserId,
        };
        bots.AddRootBot(bot);
        var granted = await GrantDefaultTools(bot, createdByUserId, cancellationToken);
        audit.Record(createdByUserId, "bot.created", AuditTargets.Bot, bot.Id, null, Snapshot(bot, [], granted));

        logger.LogInformation("Bot {BotId} created", bot.Id);
        return new BotChange(bot, null);
    }

    /// <summary>Replaces a bot's definition with the edit, when every part of the edit holds.</summary>
    public async Task<BotChange> Update(Guid id, BotEdit edit, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(id, cancellationToken) is not { } bot)
        {
            return BotChange.NotFound;
        }

        if (await FindProblem(bot, edit, cancellationToken) is { } problem)
        {
            return new BotChange(null, problem);
        }

        var before = Snapshot(bot, await bots.ListSandboxShares(id, cancellationToken));
        var sharedWith = edit.SandboxPolicy is SandboxPolicy.Named ? edit.SandboxSharedWith.Distinct().Order().ToList() : [];

        // A screen is part of the machine a sandbox is made from, so a change to it takes the bot's sandbox out of use.
        // Its next use makes one of the new kind and destroys this one; its files go with it, as the editor says.
        if (bot.SandboxSettings.Desktop != edit.SandboxSettings!.Desktop
            && await sandboxes.RetireLiveForOwner(bot.Id, time.GetUtcNow(), cancellationToken))
        {
            logger.LogInformation("Bot {BotId} changed its sandbox's screen; its sandbox is retired", bot.Id);
        }

        bot.Name = edit.Name.Trim();
        bot.Handle = edit.Handle;
        bot.Avatar = edit.Avatar;
        bot.DescriptionForBots = edit.DescriptionForBots;
        bot.Instructions = edit.Instructions;
        bot.ModelConnectionId = edit.ModelConnectionId;
        bot.Model = edit.Model.Trim();
        bot.ModelSettings = edit.ModelSettings!;
        bot.MemorySettings = edit.MemorySettings!;
        bot.SandboxPolicy = edit.SandboxPolicy!.Value;
        bot.SandboxSettings = edit.SandboxSettings! with { AllowedHosts = [.. edit.SandboxSettings.AllowedHosts.Select(host => host.Trim().ToLowerInvariant())] };
        bot.BuildLimits = edit.BuildLimits;
        await bots.ReplaceSandboxShares(id, sharedWith, cancellationToken);

        if (audit.Record(actorUserId, "bot.updated", AuditTargets.Bot, bot.Id, before, Snapshot(bot, sharedWith)))
        {
            logger.LogInformation("Bot {BotId} updated", bot.Id);
        }

        return new BotChange(bot, null) { SandboxSharedWith = sharedWith };
    }

    /// <summary>
    /// Makes a second bot from this one's definition. The copy has no parent, a name and handle of its own, and the
    /// tools this bot can use right now. Schedules are not copied: two bots answering one schedule is never wanted.
    /// </summary>
    public async Task<BotChange> Duplicate(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(id, cancellationToken) is not { } source)
        {
            return BotChange.NotFound;
        }

        var name = CopyName(source.Name);
        var copy = new Bot
        {
            Name = name,
            Handle = await FindFreeHandle(name, cancellationToken),
            Avatar = source.Avatar,
            DescriptionForBots = source.DescriptionForBots,
            Instructions = source.Instructions,
            ModelConnectionId = source.ModelConnectionId,
            Model = source.Model,
            ModelSettings = source.ModelSettings,
            MemorySettings = source.MemorySettings,
            SandboxPolicy = source.SandboxPolicy,
            SandboxSettings = source.SandboxSettings,
            BuildLimits = source.BuildLimits,
            CreatedByUserId = actorUserId,
        };
        var sharedWith = await bots.ListSandboxShares(id, cancellationToken);
        bots.AddRootBot(copy);
        await bots.ReplaceSandboxShares(copy.Id, sharedWith, cancellationToken);

        var granted = await CopyGrants(source, copy, actorUserId, cancellationToken);
        var about = new JsonObject { ["copied_from"] = source.Id };
        audit.Record(actorUserId, "bot.duplicated", AuditTargets.Bot, copy.Id, null, Snapshot(copy, sharedWith, granted), about);

        logger.LogInformation("Bot {BotId} duplicated as {CopyId}", source.Id, copy.Id);
        return new BotChange(copy, null) { SandboxSharedWith = sharedWith };
    }

    /// <summary>Pauses a bot: it starts no run and uses no tool until it is resumed.</summary>
    public Task<BotChange> Pause(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        SetStatus(id, BotStatus.Paused, "bot.paused", actorUserId, cancellationToken);

    public Task<BotChange> Resume(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        SetStatus(id, BotStatus.Active, "bot.resumed", actorUserId, cancellationToken);

    /// <summary>Removes a bot. Its row, threads, and history stay; it is no longer listed and can do nothing.</summary>
    public async Task<BotChange> Remove(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var change = await SetStatus(id, BotStatus.Removed, "bot.removed", actorUserId, cancellationToken);

        if (change.Bot is not null)
        {
            // A share that outlived its bot would name a bot no edit can keep, and make the other bot unsaveable.
            await bots.RemoveSandboxSharesOf(id, cancellationToken);
        }

        return change;
    }

    #endregion

    #region Private Methods

    /// <summary>A new bot as the audit log keeps it: its definition and the tools it was given, by name.</summary>
    private static JsonObject Snapshot(Bot bot, IReadOnlyList<Guid> sharedWith, IReadOnlyList<(string Tool, ToolGrant Grant)> granted)
    {
        var snapshot = Snapshot(bot, sharedWith);
        snapshot["grants"] = new JsonArray([.. granted.OrderBy(entry => entry.Tool, StringComparer.Ordinal).Select(entry =>
        {
            var grant = GrantService.Snapshot(entry.Grant);
            grant["tool"] = entry.Tool;
            grant["tool_id"] = entry.Grant.ToolId;
            return (JsonNode)grant;
        })]);
        return snapshot;
    }

    private static JsonObject Snapshot(Bot bot, IReadOnlyList<Guid> sharedWith) =>
        AuditLog.Snapshot(new
        {
            bot.Name,
            bot.Handle,
            bot.Avatar,
            bot.DescriptionForBots,
            bot.Instructions,
            bot.ModelConnectionId,
            bot.Model,
            bot.ModelSettings,
            bot.MemorySettings,
            bot.SandboxPolicy,
            bot.SandboxSettings,
            SandboxSharedWith = sharedWith,
            bot.BuildLimits,
            bot.Status,
        });

    private static string CopyName(string name)
    {
        const string Suffix = " copy";
        return $"{name[..Math.Min(name.Length, MaxNameLength - Suffix.Length)]}{Suffix}";
    }

    private async Task<BotChange> SetStatus(Guid id, BotStatus status, string action, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(id, cancellationToken) is not { } bot)
        {
            return BotChange.NotFound;
        }

        var before = new JsonObject { ["status"] = Words.Of(bot.Status) };
        bot.Status = status;

        if (audit.Record(actorUserId, action, AuditTargets.Bot, bot.Id, before, new JsonObject { ["status"] = Words.Of(status) }))
        {
            logger.LogInformation("Bot {BotId} is now {Status}", bot.Id, status);
        }

        return new BotChange(bot, null) { SandboxSharedWith = await bots.ListSandboxShares(id, cancellationToken) };
    }

    private async Task<string?> FindProblem(Bot bot, BotEdit edit, CancellationToken cancellationToken) =>
        FindTextProblem(edit)
        ?? await FindHandleProblem(bot, edit, cancellationToken)
        ?? await FindAvatarProblem(edit.Avatar, cancellationToken)
        ?? await FindModelProblem(edit, cancellationToken)
        ?? FindMemoryProblem(edit.MemorySettings)
        ?? await FindSandboxProblem(bot, edit, cancellationToken)
        ?? FindBuildProblem(edit.BuildLimits);

    private static string? FindTextProblem(BotEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.Name) || edit.Name.Trim().Length > MaxNameLength)
        {
            return $"Give the bot a name of up to {MaxNameLength} characters.";
        }

        if (edit.DescriptionForBots.Length > MaxDescriptionLength)
        {
            return $"Keep what this bot is for under {MaxDescriptionLength} characters.";
        }

        return edit.Instructions.Length > MaxInstructionsLength
            ? $"Keep the instructions under {MaxInstructionsLength} characters."
            : null;
    }

    private async Task<string?> FindHandleProblem(Bot bot, BotEdit edit, CancellationToken cancellationToken)
    {
        var handle = edit.Handle;
        var wellFormed = handle.Length is > 0 and <= MaxHandleLength
            && handle.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-')
            && handle[0] != '-' && handle[^1] != '-';

        if (!wellFormed)
        {
            return $"A handle is up to {MaxHandleLength} lower-case letters, digits, and hyphens, and starts and ends with a letter or digit.";
        }

        return handle != bot.Handle && await bots.HandleExists(handle, bot.Id, cancellationToken)
            ? $"Another bot already uses the handle {handle}. Choose a different one."
            : null;
    }

    private async Task<string?> FindAvatarProblem(BotAvatar avatar, CancellationToken cancellationToken)
    {
        if (avatar.Emoji is { Length: > MaxSymbolLength })
        {
            return $"Keep the avatar symbol to {MaxSymbolLength} characters.";
        }

        return avatar.FileId is { } fileId && !await bots.FileExists(fileId, cancellationToken)
            ? "The avatar picture is not a file of this space."
            : null;
    }

    private async Task<string?> FindModelProblem(BotEdit edit, CancellationToken cancellationToken)
    {
        if (await connections.FindConnection(edit.ModelConnectionId, cancellationToken) is null)
        {
            return "Choose a model connection of this space.";
        }

        if (string.IsNullOrWhiteSpace(edit.Model) || edit.Model.Trim().Length > MaxModelLength)
        {
            return "Choose a model for the bot.";
        }

        if (edit.ModelSettings is not { } settings)
        {
            return "Web search is off, on, or automatic.";
        }

        if (settings.Temperature is < 0 or > MaxTemperature)
        {
            return $"Temperature is between 0 and {MaxTemperature}.";
        }

        // These hold for every model, known or not: a setting is copied into the audit log each time it changes.
        if (settings.MaxOutputTokens is <= 0 or > MaxAnswerTokens)
        {
            return $"The longest answer is from 1 to {MaxAnswerTokens} tokens.";
        }

        if (settings.ReasoningEffort is { Length: > MaxEffortLength })
        {
            return $"A reasoning effort is one word of up to {MaxEffortLength} characters.";
        }

        // A model the catalog does not know is taken on trust; one it knows is held to what it can do.
        var known = await connections.FindCatalogEntry(edit.ModelConnectionId, edit.Model.Trim(), cancellationToken);
        return known is null ? null : FindCapabilityProblem(settings, known);
    }

    private static string? FindCapabilityProblem(ModelSettings settings, ModelCatalogEntry known)
    {
        var can = known.Capabilities;

        if (settings.MaxOutputTokens > can.MaxOutputTokens)
        {
            return $"{known.DisplayName} writes at most {can.MaxOutputTokens} tokens in one answer.";
        }

        if (settings.ReasoningEffort is { } effort && !can.ReasoningEfforts.Contains(effort, StringComparer.OrdinalIgnoreCase))
        {
            return can.ReasoningEfforts.Count == 0
                ? $"{known.DisplayName} has no reasoning effort to set."
                : $"{known.DisplayName} takes these reasoning efforts: {string.Join(", ", can.ReasoningEfforts)}.";
        }

        if (settings.WebSearch is not WebSearchMode.Off && !can.WebSearch)
        {
            return $"{known.DisplayName} has no built-in web search.";
        }

        return settings.StructuredOutput && !can.StructuredOutput
            ? $"{known.DisplayName} does not support structured output."
            : null;
    }

    private static string? FindMemoryProblem(MemorySettings? memory)
    {
        if (memory is null)
        {
            return "Choose how long threads are kept short: none, sliding_window, summarization, tool_results, or truncation.";
        }

        if (memory.CompactAtTokens is <= 0 or > MaxCompactAtTokens)
        {
            return $"The size at which a thread is shortened is above zero and at most {MaxCompactAtTokens} tokens.";
        }

        return memory.KeepRecentMessages is < 0 or > MaxKeptMessages
            ? $"The number of recent messages to keep is at most {MaxKeptMessages} and cannot be negative."
            : null;
    }

    private async Task<string?> FindSandboxProblem(Bot bot, BotEdit edit, CancellationToken cancellationToken)
    {
        if (edit.SandboxPolicy is not { } policy)
        {
            return "A sandbox is none, private, subtree, or named.";
        }

        if (edit.SandboxSettings is not { } settings)
        {
            return "Sandbox network access is off, allowlist, or open.";
        }

        // The deployment caps both: a running sandbox costs the operator money, and open egress is the operator's
        // liability on a shared server. Limits only narrow, so a setting past them is refused, not quietly lowered.
        var mostIdleMinutes = policies.Deployment.MaxSandboxIdleMinutes;

        if (settings.IdleTimeoutMinutes is <= 0 || settings.IdleTimeoutMinutes > mostIdleMinutes)
        {
            return $"A sandbox stops after 1 to {mostIdleMinutes} idle minutes on this server.";
        }

        if (settings.Network is NetworkAccess.Open && !policies.Deployment.AllowOpenSandboxNetwork)
        {
            return "This server does not let a sandbox reach the open internet. Choose Off or Named hosts.";
        }

        // A sandbox with a screen is a far larger machine; where the operator has not allowed one, it is refused.
        if (settings.Desktop && !policies.Deployment.AllowDesktopSandboxes)
        {
            return "This server does not offer sandboxes with a screen. Turn Desktop off.";
        }

        if (settings.AllowedHosts.Count > MaxAllowedHosts)
        {
            return $"A sandbox may reach at most {MaxAllowedHosts} named hosts.";
        }

        // A list in a request can hold a null where a name should be; that is refused like any other entry that is no host.
        var unreadable = settings.AllowedHosts
            .Select((host, index) => (Host: host, Index: index))
            .FirstOrDefault(entry => string.IsNullOrWhiteSpace(entry.Host) || Uri.CheckHostName(entry.Host.Trim()) is not UriHostNameType.Dns, (Host: string.Empty, Index: -1));

        if (unreadable.Index >= 0)
        {
            var named = string.IsNullOrWhiteSpace(unreadable.Host) ? "A blank entry" : unreadable.Host;
            return $"{named} is not a host name. Write it like example.com, with no address scheme or path.";
        }

        if (policy is not SandboxPolicy.Named)
        {
            return null;
        }

        var others = edit.SandboxSharedWith.Distinct().ToList();

        if (others.Contains(bot.Id))
        {
            return "A bot always has its own sandbox. Choose other bots to share it with.";
        }

        return await bots.CountBots(others, cancellationToken) != others.Count
            ? "A sandbox can be shared only with bots of this space."
            : null;
    }

    private static string? FindBuildProblem(BuildLimits limits) =>
        limits.MaxChildren is < 0 or > MaxBuildLimit || limits.MaxDepthBelow is < 0 or > MaxBuildLimit
            ? $"A build limit is a whole number from 0 to {MaxBuildLimit}."
            : null;

    private async Task<List<(string Tool, ToolGrant Grant)>> GrantDefaultTools(Bot bot, Guid grantedByUserId, CancellationToken cancellationToken)
    {
        List<(string Tool, ToolGrant Grant)> granted = [];

        foreach (var tool in await catalog.EnsureBuiltins(cancellationToken))
        {
            if (BuiltinTools.GrantedToNewBots.Contains(tool.Name, StringComparer.Ordinal))
            {
                var grant = new ToolGrant { BotId = bot.Id, ToolId = tool.Id, Effect = GrantEffect.Allow, GrantedByUserId = grantedByUserId };
                tools.Add(grant);
                granted.Add((tool.Name, grant));
            }
        }

        return granted;
    }

    /// <summary>
    /// Gives the copy the source's own grants, each with its review state, so a copy is never a way past a review.
    /// The copy stands alone at the root, so a grant the source's parent holds lower, or not at all, is lowered or
    /// left out: the copy never gets more than the source could really do.
    /// </summary>
    private async Task<List<(string Tool, ToolGrant Grant)>> CopyGrants(Bot source, Bot copy, Guid actorUserId, CancellationToken cancellationToken)
    {
        var own = await tools.ListOwnGrants(source.Id, cancellationToken);
        var ceilings = source.ParentBotId is { } parentId ? await tools.ListEffectiveGrants(parentId, cancellationToken) : null;
        var names = await tools.ListToolNames([.. own.Select(grant => grant.ToolId)], cancellationToken);
        List<(string Tool, ToolGrant Grant)> granted = [];

        foreach (var held in own)
        {
            if (ceilings is not null && !ceilings.ContainsKey(held.ToolId))
            {
                continue;
            }

            var grant = new ToolGrant
            {
                BotId = copy.Id,
                ToolId = held.ToolId,
                Effect = ceilings is null ? held.Effect : GrantRules.Min(held.Effect, ceilings[held.ToolId]),
                ReviewState = held.ReviewState,
                GrantedByUserId = actorUserId,
            };
            tools.Add(grant);
            granted.Add((names.GetValueOrDefault(held.ToolId, held.ToolId.ToString()), grant));
        }

        return granted;
    }

    private async Task<string> FindFreeHandle(string name, CancellationToken cancellationToken)
    {
        var slug = new string([.. name.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')])
            .Trim('-');
        var stem = slug.Length == 0 ? "bot" : slug[..Math.Min(slug.Length, MaxHandleLength - 8)].Trim('-');
        var handle = stem;

        for (var suffix = 2; await bots.HandleExists(handle, cancellationToken); suffix++)
        {
            handle = $"{stem}-{suffix}";
        }

        return handle;
    }

    #endregion
}
