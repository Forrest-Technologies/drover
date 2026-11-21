namespace Drover.Engine.Bots;

/// <summary>
/// Bots of the current space: reading them, and every change a person makes to one. Each change is checked here, on
/// the server, and written to the audit log in the same scope. The caller, an endpoint, owns the tenant scope.
/// </summary>
public sealed class BotService(
    BotRepository bots,
    ModelConnectionRepository connections,
    ToolRepository tools,
    BotRuleRepository rules,
    ToolCatalog catalog,
    AccountRepository accounts,
    TenantContext tenant,
    AuditLog audit,
    PolicyService policies,
    SandboxRepository sandboxes,
    ModelFallbackService fallbacks,
    TimeProvider time,
    ILogger<BotService> logger)
{
    #region Private Fields

    public const int MaxNameLength = FieldLimits.BotName;

    public const int MaxDescriptionLength = FieldLimits.BotDescription;

    public const int MaxInstructionsLength = 32_000;

    public const int MaxModelLength = FieldLimits.ModelName;

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

    /// <summary>
    /// Creates a bot, at the top of the tree or beneath a parent of this space, and gives it the tools every new bot
    /// starts with. A bot beneath a parent gets each of those tools only as far as the parent holds it now. The maker
    /// is a person, or a bot acting for one; the new bot records both.
    /// </summary>
    public async Task<BotChange> Create(NewBot request, AuditActor maker, CancellationToken cancellationToken)
    {
        var botId = (Guid?)null;

        if (request.RequestId is { } requestId)
        {
            if (requestId == Guid.Empty)
            {
                return new BotChange(null, "Choose a request id that is not empty.");
            }

            if (tenant.SpaceId is not { } spaceId)
            {
                return new BotChange(null, "Choose a space for the bot.");
            }

            botId = BotRequestIds.For(spaceId, maker, requestId);

            if (await FindReplay(botId.Value, cancellationToken) is { } replay)
            {
                return replay;
            }
        }

        var (name, description, instructions) = (request.Name, request.DescriptionForBots, request.Instructions);

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return new BotChange(null, $"Give the bot a name of up to {MaxNameLength} characters.");
        }

        if (description.Length > MaxDescriptionLength)
        {
            return new BotChange(null, $"Keep what this bot is for under {MaxDescriptionLength} characters.");
        }

        if (instructions.Length > MaxInstructionsLength)
        {
            return new BotChange(null, $"Keep the instructions under {MaxInstructionsLength} characters.");
        }

        var connection = await connections.FindConnection(request.ModelConnectionId, cancellationToken);

        if (connection is null)
        {
            return new BotChange(null, "Choose a model connection of this space.");
        }

        var chosen = string.IsNullOrWhiteSpace(request.Model) ? connection.DefaultModel : request.Model.Trim();

        if (string.IsNullOrWhiteSpace(chosen) || chosen.Length > MaxModelLength)
        {
            return new BotChange(null, "Choose a model for the bot.");
        }

        if (await FindCountProblem(cancellationToken) is { } full)
        {
            return new BotChange(null, full);
        }

        Bot? parent = null;

        if (request.ParentBotId is { } parentId)
        {
            parent = await bots.LockBot(parentId, cancellationToken);

            if (parent is null)
            {
                return new BotChange(null, "Choose a parent bot of this space.");
            }

            if (await FindTreeProblem(parent, cancellationToken) is { } problem)
            {
                return new BotChange(null, problem);
            }
        }

        var bot = new Bot
        {
            Id = botId ?? Guid.CreateVersion7(),
            Name = name.Trim(),
            Handle = await FindFreeHandle(name, null, cancellationToken),
            DescriptionForBots = description,
            Instructions = instructions,
            ModelConnectionId = connection.Id,
            Model = chosen,
            ParentBotId = parent?.Id,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Status = request.Paused ? BotStatus.Paused : BotStatus.Active,
            CreatedByUserId = maker.UserId,
            CreatedByBotId = maker.BotId,
        };
        var granted = await AddToTree(bot, parent, maker, cancellationToken);
        var about = parent is null ? null : new JsonObject { ["parent_bot_id"] = parent.Id };
        audit.Record(maker, "bot.created", AuditTargets.Bot, bot.Id, null, Snapshot(bot, [], granted), about);

        logger.LogInformation("Bot {BotId} created", bot.Id);
        return new BotChange(bot, null);
    }

    // A request with a retry identity makes a bot whose id is worked out from the space, the maker and that identity, so
    // the bot a lost answer left behind is the one a repeat of the request finds, and nothing else is: a bot is never
    // found by what it is called or says, so another person's bot of the same name is never taken for this one. The id
    // is never one the client picked, so it cannot meet a bot of another space: the same identity sent from two spaces
    // makes two bots, and what a caller sees does not depend on any other space. The tree lock is taken first, so a
    // repeat that overlaps the request it repeats waits for it to commit and then finds its bot. A bot that was removed
    // is not brought back by a repeat.
    private async Task<BotChange?> FindReplay(Guid id, CancellationToken cancellationToken)
    {
        await bots.LockTree(cancellationToken);

        if (await bots.FindBot(id, cancellationToken) is not { } held)
        {
            return null;
        }

        return held.Status is not BotStatus.Removed
            ? new BotChange(held, null)
            : new BotChange(null, "That request was already used for a bot that was removed. Start the bot again.");
    }

    /// <summary>Replaces a bot's definition with the edit, when every part of the edit holds.</summary>
    public async Task<BotChange> Update(Guid id, BotEdit edit, AuditActor actor, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(id, cancellationToken) is not { } bot)
        {
            return BotChange.NotFound;
        }

        // A handle the old name made follows the new name. The client says so when the handle it sends is the new
        // name's, not one a person chose, and it may be taken by another bot: the server then adds a number as it does
        // for a new bot. A handle a person chose is used as it came, and a taken one is refused.
        // What is stored decides before the spelling does: a handle a person set is never the name's, whatever it is
        // spelled like.
        if (edit.HandleFollowsName is true
            && !bot.HandleChosen
            && edit.Handle != bot.Handle
            && edit.Name.Trim() != bot.Name
            && BotHandles.Follows(bot.Handle, bot.Name)
            && edit.Handle == BotHandles.Stem(edit.Name)
            && await bots.HandleExists(edit.Handle, bot.Id, cancellationToken))
        {
            edit = edit with { Handle = await FindFreeHandle(edit.Name, bot.Id, cancellationToken) };
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
        // A choice is recorded and never undone by an update: a client that says the handle is the name's cannot take
        // a handle a person set back to following the name.
        bot.HandleChosen = edit.HandleFollowsName switch
        {
            true => bot.HandleChosen,
            false => true,
            null => bot.HandleChosen || edit.Handle != bot.Handle,
        };
        bot.Handle = edit.Handle;
        bot.Avatar = edit.Avatar;
        bot.DescriptionForBots = edit.DescriptionForBots;
        bot.Instructions = edit.Instructions;
        bot.ModelConnectionId = edit.ModelConnectionId;
        bot.Model = edit.Model.Trim();
        bot.ModelSettings = edit.ModelSettings! with
        {
            Fallbacks = [.. edit.ModelSettings.Fallbacks.Select(fallback => fallback with { Model = fallback.Model.Trim() })],
        };
        bot.MemorySettings = edit.MemorySettings!;
        bot.SandboxPolicy = edit.SandboxPolicy!.Value;
        bot.SandboxSettings = edit.SandboxSettings! with
        {
            AllowedHosts = [.. edit.SandboxSettings.AllowedHosts.Select(host => host.Trim().ToLowerInvariant())],
            ScreenReader = edit.SandboxSettings.ScreenReader is { } reader ? reader with { Model = reader.Model.Trim() } : null,
        };
        bot.BuildLimits = edit.BuildLimits;
        bot.RunLimits = edit.RunLimits;
        await bots.ReplaceSandboxShares(id, sharedWith, cancellationToken);

        if (audit.Record(actor, "bot.updated", AuditTargets.Bot, bot.Id, before, Snapshot(bot, sharedWith)))
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

        if (await FindCountProblem(cancellationToken) is { } full)
        {
            return new BotChange(null, full);
        }

        var name = CopyName(source.Name);
        var copy = new Bot
        {
            Name = name,
            Handle = await FindFreeHandle(name, null, cancellationToken),
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
            RunLimits = source.RunLimits,
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
    public Task<BotChange> Pause(Guid id, AuditActor actor, CancellationToken cancellationToken) =>
        SetStatus(id, BotStatus.Paused, "bot.paused", actor, cancellationToken);

    public Task<BotChange> Resume(Guid id, AuditActor actor, CancellationToken cancellationToken) =>
        SetStatus(id, BotStatus.Active, "bot.resumed", actor, cancellationToken);

    /// <summary>
    /// Removes a bot. Its row, threads, and history stay; it is no longer listed and can do nothing. A bot with bots
    /// beneath it needs a choice for them, and is refused in words without one, so no caller can skip the question:
    /// <see cref="BotsBeneath.Remove"/> removes the whole branch, each bot as this one; <see cref="BotsBeneath.Keep"/>
    /// moves the bots directly beneath it, with everything beneath them, up beneath its parent, or to the top. Either
    /// happens whole or not at all, in the caller's scope.
    /// </summary>
    public async Task<BotChange> Remove(Guid id, BotsBeneath? beneath, AuditActor actor, CancellationToken cancellationToken)
    {
        await bots.LockTree(cancellationToken);

        if (await bots.GetBotForUpdate(id, cancellationToken) is not { } bot)
        {
            return BotChange.NotFound;
        }

        var children = await bots.ListChildren(id, cancellationToken);

        if (children.Count > 0 && beneath is null)
        {
            return new BotChange(null, HasBotsBeneath(bot, children.Count));
        }

        var branch = beneath is BotsBeneath.Remove ? await bots.ListDescendantsForUpdate(id, cancellationToken) : [];

        if (beneath is BotsBeneath.Keep && children.Count > 0
            && await MoveUp(bot, children, actor, cancellationToken) is { } problem)
        {
            return new BotChange(null, problem);
        }

        await RemoveOne(bot, actor, null, cancellationToken);
        var with = new JsonObject { ["removed_with"] = bot.Id };

        foreach (var below in branch)
        {
            await RemoveOne(below, actor, with, cancellationToken);
        }

        return new BotChange(bot, null)
        {
            Beneath = beneath is BotsBeneath.Remove ? [.. branch.Select(below => below.Id)] : [.. children.Select(child => child.Id)],
        };
    }

    /// <summary>The sentence a remove is refused with when the bot has bots beneath it and no choice was made for them.</summary>
    public static string HasBotsBeneath(Bot bot, int children) =>
        $"{bot.Name} has {Count(children, "bot")} directly beneath it. Choose whether they are removed with it or kept and moved up.";

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
            bot.RunLimits,
            bot.Status,
        });

    /// <summary>
    /// Marks one bot removed, ends the sandbox shares it gives or was given, and retires its own sandbox. A share
    /// that outlived its bot would name a bot no edit can keep. The sandbox is retired, not destroyed: kept, paused,
    /// for the retention period so a person can still recover its files; the server's sweep destroys it after.
    /// </summary>
    private async Task RemoveOne(Bot bot, AuditActor actor, JsonObject? about, CancellationToken cancellationToken)
    {
        var before = new JsonObject { ["status"] = Words.Of(bot.Status) };
        bot.Status = BotStatus.Removed;
        audit.Record(actor, "bot.removed", AuditTargets.Bot, bot.Id, before, new JsonObject { ["status"] = Words.Of(BotStatus.Removed) }, about);
        await bots.RemoveSandboxSharesOf(bot.Id, cancellationToken);

        if (await sandboxes.RetireLiveForOwner(bot.Id, time.GetUtcNow() + SandboxSweep.Retention, cancellationToken))
        {
            logger.LogInformation("Bot {BotId} was removed; its sandbox is retired", bot.Id);
        }

        logger.LogInformation("Bot {BotId} is now {Status}", bot.Id, BotStatus.Removed);
    }

    /// <summary>
    /// Moves the bots directly beneath a bot being removed up beneath its parent, or to the top, each with everything
    /// beneath it. The parent's own cap on bots directly beneath it, and the space's, are checked first, and the move
    /// is refused whole when they would be exceeded. Moving up never makes a branch deeper. Each moved bot's own
    /// grants are lowered to what the removed bot could do, and one it could not use at all is taken away, so no bot
    /// of a moved branch can do more after the move than before it, and none holds more than its new parent.
    /// </summary>
    private async Task<string?> MoveUp(Bot removed, List<Bot> children, AuditActor actor, CancellationToken cancellationToken)
    {
        var newParent = removed.ParentBotId is { } parentId ? await bots.FindBot(parentId, cancellationToken) : null;

        if (newParent is not null && await FindRoomProblem(removed, newParent, children.Count, cancellationToken) is { } problem)
        {
            return problem;
        }

        var ceilings = await tools.ListEffectiveGrants(removed.Id, cancellationToken);
        var removedRules = await rules.GetRules(removed.Id, cancellationToken);

        foreach (var child in children)
        {
            await LowerGrants(child, ceilings, actor, cancellationToken);
            await CopyRules(removed, child, removedRules, actor, cancellationToken);
            await bots.MoveUp(child.Id, removed.Id, newParent?.Id, cancellationToken);
            audit.Record(
                actor,
                "bot.moved",
                AuditTargets.Bot,
                child.Id,
                new JsonObject { ["parent_bot_id"] = removed.Id },
                new JsonObject { ["parent_bot_id"] = newParent?.Id },
                new JsonObject { ["removed_bot_id"] = removed.Id });
            logger.LogInformation("Bot {BotId} moved up from beneath {RemovedBotId}", child.Id, removed.Id);
        }

        return null;
    }

    /// <summary>
    /// Why the bots beneath a removed bot cannot move up beneath its parent, or null when they can. They take the
    /// removed bot's place, so the parent's count grows by one less than the number moved; moving a single bot up
    /// never changes it, and is never refused.
    /// </summary>
    private async Task<string?> FindRoomProblem(Bot removed, Bot newParent, int moving, CancellationToken cancellationToken)
    {
        var after = await bots.CountChildren(newParent.Id, cancellationToken) - 1 + moving;
        var caps = await GetSpaceCaps(cancellationToken);
        int?[] limits = [caps.MaxChildrenPerBot, newParent.BuildLimits.MaxChildren];
        var most = limits.Min();

        return moving > 1 && most is { } cap && after > cap
            ? $"{newParent.Name} may have at most {Count(cap, "bot")} directly beneath it, so the {moving} bots beneath {removed.Name} cannot move up to it. Remove them with {removed.Name}, or remove some of them first."
            : null;
    }

    /// <summary>A moved bot's own grants, each lowered to the removed bot's effective grant, or taken away where it had none.</summary>
    private async Task LowerGrants(Bot moved, Dictionary<Guid, GrantEffect> ceilings, AuditActor actor, CancellationToken cancellationToken)
    {
        var own = await tools.ListOwnGrants(moved.Id, cancellationToken);
        var names = await tools.ListToolNames([.. own.Select(grant => grant.ToolId)], cancellationToken);

        foreach (var grant in own)
        {
            var lowered = ceilings.TryGetValue(grant.ToolId, out var ceiling) ? GrantRules.Min(grant.Effect, ceiling) : (GrantEffect?)null;

            if (lowered == grant.Effect)
            {
                continue;
            }

            var before = GrantService.Snapshot(grant);

            if (lowered is { } effect)
            {
                grant.Effect = effect;
            }
            else
            {
                tools.Remove(grant);
            }

            var about = new JsonObject { ["tool_id"] = grant.ToolId, ["tool"] = names.GetValueOrDefault(grant.ToolId, grant.ToolId.ToString()) };
            audit.Record(actor, "grant.changed", AuditTargets.Bot, moved.Id, before, GrantService.Snapshot(lowered is null ? null : grant), about);
        }
    }

    /// <summary>
    /// The removed bot's switched-on rules become the moved bot's own, ahead of the rules it already had, so a bot
    /// that moves up follows every rule it followed before. Rules switched off bound nothing and are not copied; a
    /// rule of a bot above still applies through the closure rows. Only direct children get copies.
    /// </summary>
    private async Task CopyRules(
        Bot removed,
        Bot moved,
        IReadOnlyList<BotRule> removedRules,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        var own = await rules.GetRules(moved.Id, cancellationToken);
        var position = 0;

        foreach (var source in removedRules.Where(rule => rule.Enabled))
        {
            var copy = new BotRule
            {
                BotId = moved.Id,
                Name = source.Name,
                Enabled = true,
                Checkpoint = source.Checkpoint,
                Effect = source.Effect,
                Conditions = [.. source.Conditions],
                CreatedByUserId = source.CreatedByUserId,
            };
            rules.Add(copy);
            RuleService.Place(own, copy, position++);
            var about = new JsonObject
            {
                ["rule_id"] = copy.Id,
                ["copied_from_rule_id"] = source.Id,
                ["removed_bot_id"] = removed.Id,
            };
            audit.Record(actor, "rule.added", AuditTargets.Bot, moved.Id, null, RuleService.Snapshot(copy), about);
        }
    }

    private async Task<SpaceSettings> GetSpaceCaps(CancellationToken cancellationToken) =>
        tenant.SpaceId is { } spaceId ? (await accounts.GetSpace(spaceId, cancellationToken))?.Settings ?? new() : new();

    /// <summary>
    /// Why the space cannot hold another bot, or null when it can: its bots that have not been removed, against the
    /// deployment's cap narrowed by the space's own. Takes the tree lock first, so two additions count one after the other.
    /// </summary>
    private async Task<string?> FindCountProblem(CancellationToken cancellationToken)
    {
        await bots.LockTree(cancellationToken);
        var most = policies.MaxBots(await GetSpaceCaps(cancellationToken));
        var live = await bots.CountLiveBots(cancellationToken);

        return live >= most
            ? $"This space allows at most {Count(most, "bot")}, and already has {live}. Remove a bot to make room for another."
            : null;
    }

    private static string CopyName(string name)
    {
        const string Suffix = " copy";
        return $"{name[..Math.Min(name.Length, MaxNameLength - Suffix.Length)]}{Suffix}";
    }

    private async Task<BotChange> SetStatus(Guid id, BotStatus status, string action, AuditActor actor, CancellationToken cancellationToken)
    {
        if (await bots.GetBotForUpdate(id, cancellationToken) is not { } bot)
        {
            return BotChange.NotFound;
        }

        var before = new JsonObject { ["status"] = Words.Of(bot.Status) };
        bot.Status = status;

        if (audit.Record(actor, action, AuditTargets.Bot, bot.Id, before, new JsonObject { ["status"] = Words.Of(status) }))
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
        ?? FindNoteProblem(edit.MemorySettings!)
        ?? await FindSandboxProblem(bot, edit, cancellationToken)
        ?? await FindScreenReaderProblem(edit.SandboxSettings?.ScreenReader, cancellationToken)
        ?? FindBuildProblem(edit.BuildLimits)
        ?? FindRunLimitProblem(edit.RunLimits, policies.Runs(await GetSpaceCaps(cancellationToken)));

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
        var wellFormed = handle.Length is > 0 and <= BotHandles.MaxLength
            && handle.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-')
            && handle[0] != '-' && handle[^1] != '-';

        if (!wellFormed)
        {
            return $"A handle is up to {BotHandles.MaxLength} lower-case letters, digits, and hyphens, and starts and ends with a letter or digit.";
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
        var capabilityProblem = known is null ? null : FindCapabilityProblem(settings, known);
        return capabilityProblem ?? await fallbacks.FindProblem(
            settings.Fallbacks,
            new ModelFallback(edit.ModelConnectionId, edit.Model.Trim()),
            cancellationToken);
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

    /// <summary>
    /// The limits on notes only narrow the server's: a note held to more than the server allows is refused, not
    /// quietly lowered.
    /// </summary>
    private string? FindNoteProblem(MemorySettings memory)
    {
        var deployment = policies.Deployment;

        if (memory.MaxNoteCharacters is { } note && (note <= 0 || note > deployment.MaxMemoryNoteCharacters))
        {
            return $"A note may hold at most {deployment.MaxMemoryNoteCharacters} characters on this server, and more than none.";
        }

        return memory.MaxNotesCharacters is { } notes && (notes <= 0 || notes > deployment.MaxMemoryCharactersPerPerson)
            ? $"A bot's notes about one person may hold at most {deployment.MaxMemoryCharactersPerPerson} characters on this server, and more than none."
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

    /// <summary>
    /// A model that reads the screen for the bot must be one of this space's, and one the catalog says reads images:
    /// a model that cannot would describe nothing, and one Drover knows nothing about is not trusted to.
    /// </summary>
    private async Task<string?> FindScreenReaderProblem(ScreenReader? reader, CancellationToken cancellationToken)
    {
        if (reader is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(reader.Model) || reader.Model.Trim().Length > MaxModelLength)
        {
            return "Choose a model to read the screen, or let the bot's own model do it.";
        }

        if (await connections.FindConnection(reader.ModelConnectionId, cancellationToken) is null)
        {
            return "The model chosen to read the screen is not on a model connection of this space. Choose another, or let the bot's own model do it.";
        }

        var model = reader.Model.Trim();
        return await connections.FindCatalogEntry(reader.ModelConnectionId, model, cancellationToken) switch
        {
            null => $"Drover does not know whether {model} can read images, so it cannot read the screen. Choose a model its connection lists as reading images.",
            { Capabilities.Vision: false } known => $"{known.DisplayName} cannot read images, so it cannot read the screen. Choose a model that can.",
            _ => null,
        };
    }

    private static string? FindBuildProblem(BuildLimits limits) =>
        limits.MaxChildren is < 0 or > MaxBuildLimit || limits.MaxDepthBelow is < 0 or > MaxBuildLimit
            ? $"A build limit is a whole number from 0 to {MaxBuildLimit}."
            : null;

    /// <summary>
    /// A bot's own limit only lowers its space's, so one above the space's is refused in words, not stored and
    /// silently ignored. A run makes at least one model call; a bot may be kept from handing work over at all.
    /// </summary>
    private static string? FindRunLimitProblem(RunLimits limits, RunCeilings space) =>
        limits switch
        {
            { MaxSteps: { } steps } when steps < 1 || steps > space.MaxSteps =>
                $"The limit on model calls is a whole number from 1 to {space.MaxSteps}, the most this space allows.",
            { MaxTokens: { } tokens } when tokens < 1 || tokens > space.MaxTokens =>
                $"The limit on tokens is a whole number from 1 to {space.MaxTokens:N0}, the most this space allows.",
            { MaxDelegations: { } delegations } when delegations < 0 || delegations > space.MaxDelegations =>
                $"The limit on handoffs is a whole number from 0 to {space.MaxDelegations}, the most this space allows.",
            _ => null,
        };

    /// <summary>
    /// Why a bot cannot be added beneath this parent, or null when it can: the space's caps on depth and on bots
    /// beneath one bot, the parent's own limit on its children, and each bot above's limit on how deep beneath it
    /// the tree may go. A cap left empty does not limit.
    /// </summary>
    private async Task<string?> FindTreeProblem(Bot parent, CancellationToken cancellationToken)
    {
        var caps = await GetSpaceCaps(cancellationToken);
        var depth = parent.Depth + 1;

        if (caps.MaxBotDepth is { } maxDepth && depth > maxDepth)
        {
            return $"This space allows bots at most {Count(maxDepth, "level")} below the top, so {parent.Name} cannot have a bot beneath it.";
        }

        var children = await bots.CountChildren(parent.Id, cancellationToken);

        if (caps.MaxChildrenPerBot is { } maxChildren && children >= maxChildren)
        {
            return $"This space allows at most {Count(maxChildren, "bot")} directly beneath one bot, and {parent.Name} already has {children}.";
        }

        if (parent.BuildLimits.MaxChildren is { } ownMax && children >= ownMax)
        {
            return $"{parent.Name} may have at most {Count(ownMax, "bot")} directly beneath it, and already has {children}.";
        }

        var limiting = (await bots.ListSelfAndAncestors(parent.Id, cancellationToken))
            .FirstOrDefault(above => above.BuildLimits.MaxDepthBelow is { } below && depth - above.Depth > below);

        return limiting is { BuildLimits.MaxDepthBelow: { } limit }
            ? $"{limiting.Name} allows bots at most {Count(limit, "level")} beneath it."
            : null;
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Places the bot in the tree and gives it the tools every new bot starts with, as far as its parent holds them.</summary>
    private async Task<List<(string Tool, ToolGrant Grant)>> AddToTree(Bot bot, Bot? parent, AuditActor maker, CancellationToken cancellationToken)
    {
        if (parent is null)
        {
            bots.AddRootBot(bot);
            return await GrantDefaultTools(bot, maker, null, cancellationToken);
        }

        await bots.AddChildBot(bot, cancellationToken);
        var ceilings = await tools.ListEffectiveGrants(parent.Id, cancellationToken);
        return await GrantDefaultTools(bot, maker, ceilings, cancellationToken);
    }

    /// <summary>
    /// Grants the tools every new bot starts with. With <paramref name="ceilings"/>, the parent's effective grants, a
    /// tool the parent lacks is left out and one it holds lower is lowered to match: a child never starts above its
    /// parent. A bot at the top also gets the crew tools as ask, where this server allows bots to build bots.
    /// </summary>
    private async Task<List<(string Tool, ToolGrant Grant)>> GrantDefaultTools(
        Bot bot,
        AuditActor maker,
        Dictionary<Guid, GrantEffect>? ceilings,
        CancellationToken cancellationToken)
    {
        List<(string Tool, ToolGrant Grant)> granted = [];

        foreach (var tool in await catalog.EnsureBuiltins(cancellationToken))
        {
            if (StartingEffect(tool.Name, atTheTop: ceilings is null) is not { } standard)
            {
                continue;
            }

            if (ceilings is not null && !ceilings.ContainsKey(tool.Id))
            {
                continue;
            }

            var grant = new ToolGrant
            {
                BotId = bot.Id,
                ToolId = tool.Id,
                Effect = ceilings is null ? standard : GrantRules.Min(standard, ceilings[tool.Id]),
                GrantedByUserId = maker.UserId,
                GrantedByBotId = maker.BotId,
            };
            tools.Add(grant);
            granted.Add((tool.Name, grant));
        }

        return granted;
    }

    /// <summary>The effect a new bot starts with on a built-in tool, or null when it starts without the tool.</summary>
    private GrantEffect? StartingEffect(string toolName, bool atTheTop)
    {
        if (BuiltinTools.GrantedToNewBots.Contains(toolName, StringComparer.Ordinal))
        {
            return GrantEffect.Allow;
        }

        return atTheTop
            && policies.Deployment.AllowBotsBuildingBots
            && BuiltinTools.AskedOfNewTopBots.Contains(toolName, StringComparer.Ordinal)
                ? GrantEffect.Ask
                : null;
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

    // The handle a name makes, with a number added while another bot holds it. A bot being renamed does not count
    // against itself.
    private async Task<string> FindFreeHandle(string name, Guid? exceptBotId, CancellationToken cancellationToken)
    {
        var stem = BotHandles.Stem(name);
        var handle = stem;

        for (var suffix = 2; await HandleTaken(handle, exceptBotId, cancellationToken); suffix++)
        {
            handle = $"{stem}-{suffix}";
        }

        return handle;
    }

    private Task<bool> HandleTaken(string handle, Guid? exceptBotId, CancellationToken cancellationToken) =>
        exceptBotId is { } id
            ? bots.HandleExists(handle, id, cancellationToken)
            : bots.HandleExists(handle, cancellationToken);

    #endregion
}
