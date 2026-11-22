namespace Drover.Engine.Sandboxes;

/// <summary>The result of a change to a sandbox secret. With neither a secret nor a problem, there is no such secret.</summary>
public sealed record SandboxSecretChange(SandboxSecretResponse? Secret, string? Problem)
{
    public static SandboxSecretChange NotFound { get; } = new(null, null);

    public static SandboxSecretChange Refused(string problem) => new(null, problem);
}


/// <summary>
/// Credentials a person gives bots for their sandboxes. The value goes into the vault and is never read back here:
/// every method returns only what may be shown. Owners and admins call it; every change is audited with the change,
/// and no audit row holds a value or anything derived from one. Runs inside the caller's tenant scope.
/// </summary>
public sealed class SandboxSecretService(
    SandboxSecretRepository secrets,
    CredentialBrokerRepository brokers,
    SandboxRepository sandboxes,
    BotRepository bots,
    ProjectRepository projects,
    AuditRepository auditEntries,
    ISecretVault vault,
    PolicyService policy,
    AuditLog audit,
    TimeProvider time,
    ILogger<SandboxSecretService> logger)
{
    #region Private Fields

    private const string ConfirmRawSentence =
        "Keeping the real value in the sandbox must be confirmed: a bot steered by something it reads could send it anywhere its network allows.";

    #endregion

    #region Public Methods

    public async Task<List<SandboxSecretResponse>> List(CancellationToken cancellationToken)
    {
        var found = await secrets.ListSecrets(cancellationToken);
        var names = await Names(cancellationToken);
        var projectNames = await ProjectNames(cancellationToken);
        return [.. found.Select(entry => entry.ToResponse(
            names,
            entry.Secret.ProjectId is { } projectId ? projectNames.GetValueOrDefault(projectId) : null))];
    }

    public async Task<SandboxSecretChange> Create(CreateSandboxSecretRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var description = request.Description?.Trim() ?? string.Empty;
        var hosts = NormalizeHosts(request.Hosts);
        var botIds = Distinct(request.BotIds);

        if (FirstProblem(
                BotCountProblem(botIds),
                SandboxSecretRules.NameProblem(name),
                SandboxSecretRules.ValueProblem(request.Value),
                SandboxSecretRules.DescriptionProblem(description),
                ParseMode(request.Mode, out var mode),
                ParseScope(request.Scope, out var scope)) is { } problem)
        {
            return SandboxSecretChange.Refused(problem);
        }

        if ((BindingProblem(mode, hosts, scope, request.ConfirmRawMode) ?? SandboxSecretRules.BrokeredValueProblem(mode, request.Value)) is { } binding)
        {
            return SandboxSecretChange.Refused(binding);
        }

        await secrets.LockSpaceSecrets(cancellationToken);

        if (await TargetProblem(scope, botIds, request.ProjectId, cancellationToken) is { } target)
        {
            return SandboxSecretChange.Refused(target);
        }

        var limit = policy.Deployment.MaxSandboxSecretsPerSpace;

        if (await secrets.CountSecrets(cancellationToken) >= limit)
        {
            return SandboxSecretChange.Refused($"A space on this server holds at most {limit} sandbox secrets. Remove one first.");
        }

        var secret = new SandboxSecret
        {
            Name = name,
            Description = description,
            Mode = mode,
            Hosts = mode is SandboxSecretMode.Brokered ? hosts : [],
            AllowInQuery = mode is SandboxSecretMode.Brokered && request.AllowInQuery,
            AllowInBody = mode is SandboxSecretMode.Brokered && request.AllowInBody,
            Scope = scope,
            ProjectId = scope is SandboxSecretScope.Project ? request.ProjectId : null,
            CreatedByUserId = actorUserId,
            CreatedAt = time.GetUtcNow(),
        };
        var targetBots = scope is SandboxSecretScope.Bots ? botIds : [];

        if (await ClashProblem(secret, targetBots, cancellationToken) is { } clash)
        {
            return SandboxSecretChange.Refused(clash);
        }

        secret.SecretId = await vault.Store(SecretKind.SandboxEnvironment, request.Value, null, cancellationToken);
        secrets.Add(secret);
        await secrets.SetBots(secret, targetBots, cancellationToken);
        audit.Record(actorUserId, "sandbox_secret.created", AuditTargets.SandboxSecret, secret.Id, null, Snapshot(secret, targetBots), About(secret));

        logger.LogInformation("Sandbox secret {SecretId} added", secret.Id);
        return await Changed(secret, targetBots, cancellationToken);
    }

    /// <summary>Replaces the value. The row keeps its id, name and scope; the old value no longer reveals.</summary>
    public async Task<SandboxSecretChange> Rotate(Guid id, RotateSandboxSecretRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return SandboxSecretChange.NotFound;
        }

        if ((SandboxSecretRules.ValueProblem(request.Value) ?? SandboxSecretRules.BrokeredValueProblem(secret.Mode, request.Value)) is { } problem)
        {
            return SandboxSecretChange.Refused(problem);
        }

        await vault.Replace(secret.SecretId, request.Value, cancellationToken);
        secret.RotatedAt = time.GetUtcNow();
        secret.RotatedByUserId = actorUserId;
        audit.Record(actorUserId, "sandbox_secret.rotated", AuditTargets.SandboxSecret, secret.Id, null, null, About(secret));

        logger.LogInformation("Sandbox secret {SecretId} replaced", secret.Id);
        return await Changed(secret, await secrets.ListBotIds(secret.Id, cancellationToken), cancellationToken);
    }

    public async Task<SandboxSecretChange> ChangeScope(Guid id, ChangeSandboxSecretScopeRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        await secrets.LockSpaceSecrets(cancellationToken);

        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return SandboxSecretChange.NotFound;
        }

        if (ParseScope(request.Scope, out var scope) is { } parse)
        {
            return SandboxSecretChange.Refused(parse);
        }

        var botIds = Distinct(request.BotIds);

        if (FirstProblem(
                BotCountProblem(botIds),
                SandboxSecretRules.BindingProblem(secret.Mode, secret.Hosts, scope),
                await TargetProblem(scope, botIds, request.ProjectId, cancellationToken)) is { } problem)
        {
            return SandboxSecretChange.Refused(problem);
        }

        var current = await secrets.ListBotIds(secret.Id, cancellationToken);
        var targetBots = scope is SandboxSecretScope.Bots ? botIds : [];

        if (secret.Mode is SandboxSecretMode.Raw && !request.ConfirmRawMode && Widens(secret.Scope, current, scope, targetBots))
        {
            return SandboxSecretChange.Refused(ConfirmRawSentence);
        }

        var before = Snapshot(secret, current);
        var previousScope = secret.Scope;
        secret.Scope = scope;
        secret.ProjectId = scope is SandboxSecretScope.Project ? request.ProjectId : null;

        if (await ClashProblem(secret, targetBots, cancellationToken) is { } clash)
        {
            return SandboxSecretChange.Refused(clash);
        }

        await secrets.SetBots(secret, targetBots, cancellationToken);

        if (secret.Mode is SandboxSecretMode.Raw && Widens(scope, targetBots, previousScope, current))
        {
            await RetireRawHolders(secret, cancellationToken);
        }

        audit.Record(actorUserId, "sandbox_secret.scope_changed", AuditTargets.SandboxSecret, secret.Id, before, Snapshot(secret, targetBots), About(secret));
        return await Changed(secret, targetBots, cancellationToken);
    }

    public async Task<SandboxSecretChange> ChangeBinding(Guid id, ChangeSandboxSecretBindingRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return SandboxSecretChange.NotFound;
        }

        if (ParseMode(request.Mode, out var mode) is { } parse)
        {
            return SandboxSecretChange.Refused(parse);
        }

        var hosts = NormalizeHosts(request.Hosts);
        var confirmed = request.ConfirmRawMode || secret.Mode is SandboxSecretMode.Raw;

        if (BindingProblem(mode, hosts, secret.Scope, confirmed) is { } problem)
        {
            return SandboxSecretChange.Refused(problem);
        }

        if (secret.Mode is SandboxSecretMode.Raw && mode is SandboxSecretMode.Brokered
            && SandboxSecretRules.BrokeredValueProblem(mode, await vault.Reveal(secret.SecretId, cancellationToken)) is { } multiline)
        {
            return SandboxSecretChange.Refused(multiline);
        }

        var botIds = await secrets.ListBotIds(secret.Id, cancellationToken);
        var before = Snapshot(secret, botIds);

        if (secret.Mode is SandboxSecretMode.Raw && mode is not SandboxSecretMode.Raw)
        {
            await RetireRawHolders(secret, cancellationToken);
        }

        secret.Mode = mode;
        secret.Hosts = mode is SandboxSecretMode.Brokered ? hosts : [];
        secret.AllowInQuery = mode is SandboxSecretMode.Brokered && request.AllowInQuery;
        secret.AllowInBody = mode is SandboxSecretMode.Brokered && request.AllowInBody;
        audit.Record(actorUserId, "sandbox_secret.binding_changed", AuditTargets.SandboxSecret, secret.Id, before, Snapshot(secret, botIds), About(secret));
        return await Changed(secret, botIds, cancellationToken);
    }

    public async Task<SandboxSecretChange> ChangeDescription(Guid id, ChangeSandboxSecretDescriptionRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return SandboxSecretChange.NotFound;
        }

        var description = request.Description?.Trim() ?? string.Empty;

        if (SandboxSecretRules.DescriptionProblem(description) is { } problem)
        {
            return SandboxSecretChange.Refused(problem);
        }

        var botIds = await secrets.ListBotIds(secret.Id, cancellationToken);
        var before = Snapshot(secret, botIds);
        secret.Description = description;
        audit.Record(actorUserId, "sandbox_secret.description_changed", AuditTargets.SandboxSecret, secret.Id, before, Snapshot(secret, botIds), About(secret));
        return await Changed(secret, botIds, cancellationToken);
    }

    /// <summary>Removes the secret and its value together. Returns false when there is no such secret.</summary>
    public async Task<bool> Delete(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return false;
        }

        await Remove(secret, AuditActor.Person(actorUserId), cancellationToken);
        logger.LogInformation("Sandbox secret {SecretId} removed", secret.Id);
        return true;
    }

    /// <summary>
    /// Takes out of use every live sandbox of a bot this secret reaches, so the next use starts one with the secrets as
    /// they are now. Their files go with them. How many, or null when there is no such secret.
    /// </summary>
    public async Task<int?> RestartSandboxes(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is not { } secret)
        {
            return null;
        }

        var owners = await secrets.ListOwnersReached(secret, cancellationToken);
        var count = 0;

        foreach (var owner in owners)
        {
            count += await sandboxes.RetireLiveForOwner(owner, time.GetUtcNow(), cancellationToken) ? 1 : 0;
        }

        audit.Record(actorUserId, "sandbox_secret.sandboxes_restarted", AuditTargets.SandboxSecret, secret.Id, null, null, About(secret));
        logger.LogInformation("Sandbox secret {SecretId}: {Count} sandboxes taken out of use", secret.Id, count);
        return count;
    }

    /// <summary>Removes every secret of a project, with their values, as the project is removed.</summary>
    public async Task RemoveForProject(Guid projectId, AuditActor actor, CancellationToken cancellationToken)
    {
        foreach (var secret in await secrets.GetProjectSecrets(projectId, cancellationToken))
        {
            await Remove(secret, actor, cancellationToken);
        }
    }

    /// <summary>What was done to the secret, newest first, or null when there is no such secret.</summary>
    public async Task<List<SandboxSecretHistoryEntry>?> History(Guid id, CancellationToken cancellationToken)
    {
        if (await secrets.GetSecret(id, cancellationToken) is null)
        {
            return null;
        }

        var entries = await auditEntries.ListEntriesAbout(id, cancellationToken);
        var people = await secrets.ListDisplayNames(
            [.. entries.Where(entry => entry.ActorKind is ActorKind.User).Select(entry => entry.ActorId ?? Guid.Empty)],
            cancellationToken);
        var botNames = await Names(cancellationToken);

        return [.. entries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new SandboxSecretHistoryEntry(
                entry.Action,
                SandboxSecretViews.Wire(entry.ActorKind),
                entry.ActorId ?? Guid.Empty,
                ActorName(entry, people, botNames),
                entry.CreatedAt))];
    }

    /// <summary>
    /// The names a bot's sandbox is given, without values, the narrowest for each name, or null when there is no
    /// such bot. Project secrets are listed with their project, since which one applies depends on the work.
    /// </summary>
    public async Task<List<BotSandboxSecretName>?> NamesForBot(Guid botId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is null)
        {
            return null;
        }

        var reaching = await secrets.ListSecretsReachingBot(botId, cancellationToken);
        var projectNames = await ProjectNames(cancellationToken);
        var forBot = reaching.Where(secret => secret.Scope is SandboxSecretScope.Bots).Select(secret => secret.Name).ToHashSet(StringComparer.Ordinal);

        return [.. reaching
            .Where(secret => secret.Scope is not SandboxSecretScope.Space || !forBot.Contains(secret.Name))
            .Select(secret => new BotSandboxSecretName(
                secret.Name,
                secret.Description,
                SandboxSecretViews.Wire(secret.Mode),
                secret.Hosts,
                secret.Scope is SandboxSecretScope.Bots ? "bot" : SandboxSecretViews.Wire(secret.Scope),
                secret.ProjectId is { } projectId ? projectNames.GetValueOrDefault(projectId) : null))];
    }

    #endregion

    #region Validation

    /// <summary>Whether a new scope reaches a bot the old one did not.</summary>
    private static bool Widens(SandboxSecretScope from, IReadOnlyList<Guid> fromBots, SandboxSecretScope to, IReadOnlyList<Guid> toBots) =>
        from is not SandboxSecretScope.Space
        && (to is SandboxSecretScope.Space || from is not SandboxSecretScope.Bots || toBots.Except(fromBots).Any());

    private static string? BotCountProblem(List<Guid> botIds) =>
        botIds.Count > SandboxSecretRules.MaxBots
            ? $"A secret can be given to at most {SandboxSecretRules.MaxBots} chosen bots. Give it to every bot instead."
            : null;

    private static string? FirstProblem(params string?[] problems) => problems.FirstOrDefault(problem => problem is not null);

    private static string? BindingProblem(SandboxSecretMode mode, List<string> hosts, SandboxSecretScope scope, bool confirmed) =>
        SandboxSecretRules.BindingProblem(mode, hosts, scope)
        ?? (mode is SandboxSecretMode.Raw && !confirmed ? ConfirmRawSentence : null);

    private static string? ParseMode(string? text, out SandboxSecretMode mode) =>
        SandboxSecretViews.TryParse(text, out mode) ? null : "Choose whether the value goes through the broker or is kept in the sandbox.";

    private static string? ParseScope(string? text, out SandboxSecretScope scope) =>
        SandboxSecretViews.TryParse(text, out scope) ? null : "Choose who may use the secret: every bot, chosen bots, or a project.";

    private async Task<string?> TargetProblem(SandboxSecretScope scope, List<Guid> botIds, Guid? projectId, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case SandboxSecretScope.Bots when botIds.Count == 0:
                return "Choose at least one bot, or give the secret to every bot.";
            case SandboxSecretScope.Bots when await bots.CountBots(botIds, cancellationToken) != botIds.Count:
                return "Choose bots from this space.";
            case SandboxSecretScope.Project when projectId is not { } id || await projects.FindProject(id, cancellationToken) is null:
                return "Choose a project from this space.";
            case SandboxSecretScope.Space or SandboxSecretScope.Project when botIds.Count > 0:
                return "Bots are chosen only for a secret given to chosen bots.";
            default:
                return null;
        }
    }

    private async Task<string?> ClashProblem(SandboxSecret secret, List<Guid> botIds, CancellationToken cancellationToken)
    {
        var clash = secret.Scope is SandboxSecretScope.Bots
            ? await secrets.AnyBotHasName(secret.Name, botIds, secret.Id, cancellationToken)
            : await secrets.AnyWithName(secret.Name, secret.Scope, secret.ProjectId, secret.Id, cancellationToken);

        return clash
            ? secret.Scope switch
            {
                SandboxSecretScope.Space => $"Another secret is already called {secret.Name} for every bot. Replace its value instead.",
                SandboxSecretScope.Project => $"Another secret is already called {secret.Name} in this project. Replace its value instead.",
                _ => $"One of these bots already has a secret called {secret.Name}. Replace its value, or choose other bots.",
            }
            : null;
    }

    #endregion

    #region Private Methods

    private static List<string> NormalizeHosts(IReadOnlyList<string>? hosts) =>
        [.. (hosts ?? []).Select(SandboxSecretRules.NormalizeHost).Where(host => host.Length > 0).Distinct(StringComparer.Ordinal)];

    private static List<Guid> Distinct(IReadOnlyList<Guid>? ids) => [.. (ids ?? []).Distinct()];

    private static JsonObject Snapshot(SandboxSecret secret, IReadOnlyList<Guid> botIds) =>
        AuditLog.Snapshot(new
        {
            secret.Name,
            secret.Description,
            secret.Mode,
            Hosts = string.Join(", ", secret.Hosts),
            secret.AllowInQuery,
            secret.AllowInBody,
            secret.Scope,
            BotIds = string.Join(", ", botIds.Order()),
            secret.ProjectId,
        });

    private static JsonObject About(SandboxSecret secret) => new() { ["name"] = secret.Name };

    private static string? ActorName(AuditLogEntry entry, Dictionary<Guid, string> people, Dictionary<Guid, string> botNames) =>
        entry.ActorId is not { } id
            ? null
            : entry.ActorKind is ActorKind.Bot ? botNames.GetValueOrDefault(id) : people.GetValueOrDefault(id);

    private async Task Remove(SandboxSecret secret, AuditActor actor, CancellationToken cancellationToken)
    {
        await RetireRawHolders(secret, cancellationToken);
        var botIds = await secrets.ListBotIds(secret.Id, cancellationToken);
        await vault.Delete(secret.SecretId, cancellationToken);
        secrets.Remove(secret);
        audit.Record(actor, "sandbox_secret.deleted", AuditTargets.SandboxSecret, secret.Id, Snapshot(secret, botIds), null, About(secret));
    }

    /// <summary>
    /// Takes out of use every live sandbox that holds this secret's real value, so it does not outlive a removal or a
    /// narrowing. The next use starts a sandbox without it.
    /// </summary>
    private async Task RetireRawHolders(SandboxSecret secret, CancellationToken cancellationToken)
    {
        foreach (var sandbox in await brokers.ListLiveSandboxesHoldingRaw(secret.Id, cancellationToken))
        {
            sandbox.State = SandboxState.Retiring;
            sandbox.RetireAfter = time.GetUtcNow();
            logger.LogInformation("Sandbox {SandboxId} taken out of use: it held the value of sandbox secret {SecretId}", sandbox.Id, secret.Id);
        }
    }

    private async Task<Dictionary<Guid, string>> Names(CancellationToken cancellationToken) =>
        (await bots.ListBotsWithRemoved(cancellationToken)).ToDictionary(bot => bot.Id, bot => bot.Name);

    private async Task<Dictionary<Guid, string>> ProjectNames(CancellationToken cancellationToken) =>
        (await projects.ListProjects(cancellationToken)).ToDictionary(project => project.Id, project => project.Name);

    private async Task<SandboxSecretChange> Changed(SandboxSecret secret, IReadOnlyList<Guid> botIds, CancellationToken cancellationToken)
    {
        var names = await Names(cancellationToken);
        var project = secret.ProjectId is { } projectId ? await projects.FindProject(projectId, cancellationToken) : null;
        return new(new SandboxSecretWithBots(secret, botIds).ToResponse(names, project?.Name), null);
    }

    #endregion
}
