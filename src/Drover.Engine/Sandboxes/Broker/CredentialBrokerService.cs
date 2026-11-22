namespace Drover.Engine.Sandboxes.Broker;

/// <summary>A sandbox the broker is serving, as it stood when its connection was opened.</summary>
public sealed record BrokerSession(Guid SpaceId, Guid SandboxId, Guid OwnerBotId, NetworkPolicy Network, IReadOnlyList<SandboxSecret> Brokered)
{
    #region Public Methods

    /// <summary>Whether a brokered secret this sandbox has may be sent to the host, so the broker must read its traffic.</summary>
    public bool IsBound(string host) => Brokered.Any(secret => SandboxSecretRules.Covers(secret.Hosts, host));

    /// <summary>
    /// Whether the sandbox may reach the host at all: a host a secret is bound to always, since binding the secret names
    /// it; any other as the bot's network setting says.
    /// </summary>
    public bool MayReach(string host) => IsBound(host) || Network.Access switch
    {
        NetworkAccess.Open => true,
        NetworkAccess.Allowlist => SandboxSecretRules.Covers(Network.AllowedHosts.Select(SandboxSecretRules.NormalizeHost), host),
        _ => false,
    };

    #endregion
}


/// <summary>Where the broker found a placeholder in a request.</summary>
public enum BrokerPlacement
{
    Header,
    Query,
    Body,
}


/// <summary>The real values for the placeholders of one request, or why the request may not go.</summary>
public sealed record BrokerSubstitution(IReadOnlyDictionary<string, string> Values, string? Refusal)
{
    public static BrokerSubstitution Refused(string refusal) => new(new Dictionary<string, string>(), refusal);
}


/// <summary>
/// The credential broker's decisions, made against the database as each request arrives: which sandbox a token is,
/// whether a host may be reached, and which real value a placeholder stands for on a given host. Nothing is cached
/// across connections, so a secret removed, rescoped or rebound stops at once. Runs in the sandbox's space with no
/// person set. Never logs a value, a placeholder or a token.
/// </summary>
public sealed class CredentialBrokerService(
    RunScopes scopes,
    BrokerAuthorities authorities,
    SandboxCommandProjects projects,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<CredentialBrokerService> logger)
{
    #region Private Fields

    private static readonly TimeSpan useRecordedEvery = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<(Guid SandboxId, Guid SecretId), DateTimeOffset> recordedUses = new();

    #endregion

    #region Public Methods

    /// <summary>The live sandbox this token belongs to, or null.</summary>
    public async Task<BrokerSession?> Authenticate(string token, CancellationToken cancellationToken)
    {
        var hash = BrokerTokens.Hash(token);
        var found = await scopes.WithoutTenant(
            services => services.GetRequiredService<CredentialBrokerRepository>().FindSandboxForToken(hash, cancellationToken),
            cancellationToken);

        return found is null
            ? null
            : await scopes.For(found.SpaceId, Audience.Everyone).Transact(services => Open(services, found, cancellationToken), cancellationToken);
    }

    /// <summary>The certificate the broker shows the sandbox for a bound host, or null when the space has no authority.</summary>
    public Task<X509Certificate2?> HostCertificate(BrokerSession session, string host, CancellationToken cancellationToken) =>
        scopes.For(session.SpaceId, Audience.Everyone).Transact(
            services => authorities.HostCertificate(services, session.SpaceId, host, cancellationToken),
            cancellationToken);

    /// <summary>
    /// The real values of every brokered secret this sandbox has that is bound to <paramref name="host"/>, for scrubbing
    /// everything the host returns on a connection: a value one request stored there must not come back on another.
    /// </summary>
    public Task<IReadOnlyList<string>> ValuesBoundTo(BrokerSession session, string host, CancellationToken cancellationToken) =>
        scopes.For(session.SpaceId, Audience.Everyone).Transact<IReadOnlyList<string>>(
            async services =>
            {
                var vault = services.GetRequiredService<ISecretVault>();
                List<string> values = [];

                foreach (var secret in session.Brokered.Where(secret => SandboxSecretRules.Covers(secret.Hosts, host)))
                {
                    values.Add(await vault.Reveal(secret.SecretId, cancellationToken));
                }

                return values;
            },
            cancellationToken);

    /// <summary>
    /// The real value for each placeholder, for a request to <paramref name="host"/>, each with where in the
    /// request it was found. Refused, with a sentence for the sandbox, when a placeholder is not this
    /// sandbox's, its secret no longer reaches the bot, is not bound to the host, may not go where it was put, or is a
    /// project's secret while no command of that project is running.
    /// </summary>
    public async Task<BrokerSubstitution> Substitute(
        BrokerSession session,
        string host,
        IReadOnlyList<(string Placeholder, BrokerPlacement Placement)> found,
        CancellationToken cancellationToken)
    {
        if (found.Count == 0)
        {
            return new(new Dictionary<string, string>(), null);
        }

        return await scopes.For(session.SpaceId, Audience.Everyone).Transact(
            services => Resolve(services, session, host, found, cancellationToken),
            cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task<BrokerSession?> Open(IServiceProvider services, BrokerSandbox found, CancellationToken cancellationToken)
    {
        var sandbox = await services.GetRequiredService<SandboxRepository>().FindSandbox(found.SandboxId, cancellationToken);

        if (sandbox is not { State: SandboxState.Creating or SandboxState.Running or SandboxState.Paused })
        {
            return null;
        }

        if (await services.GetRequiredService<BotRepository>().FindBot(sandbox.OwnerBotId, cancellationToken) is not { Status: not BotStatus.Removed } owner)
        {
            return null;
        }

        var network = owner.SandboxSettings.Network is NetworkAccess.Open && !deployment.Value.Rules.AllowOpenSandboxNetwork
            ? new NetworkPolicy(NetworkAccess.Off, [])
            : owner.SandboxSettings.ToNetworkPolicy();
        var brokered = (await services.GetRequiredService<SandboxSecretRepository>().ListSecretsReachingBot(owner.Id, cancellationToken))
            .Where(secret => secret.Mode is SandboxSecretMode.Brokered)
            .ToList();
        return new BrokerSession(found.SpaceId, sandbox.Id, owner.Id, network, brokered);
    }

    private async Task<BrokerSubstitution> Resolve(
        IServiceProvider services,
        BrokerSession session,
        string host,
        IReadOnlyList<(string Placeholder, BrokerPlacement Placement)> found,
        CancellationToken cancellationToken)
    {
        var brokers = services.GetRequiredService<CredentialBrokerRepository>();
        var reaching = (await services.GetRequiredService<SandboxSecretRepository>().ListSecretsReachingBot(session.OwnerBotId, cancellationToken))
            .Select(secret => secret.Id)
            .ToHashSet();
        var vault = services.GetRequiredService<ISecretVault>();
        Dictionary<string, string> values = [];
        List<SandboxSecret> used = [];

        foreach (var (placeholder, placement) in found.DistinctBy(item => (item.Placeholder, item.Placement)))
        {
            var candidates = (await brokers.ListSecretsForPlaceholder(session.SandboxId, BrokerTokens.Hash(placeholder), cancellationToken))
                .Where(secret => reaching.Contains(secret.Id) && secret.Mode is SandboxSecretMode.Brokered)
                .ToList();

            if (Pick(candidates, session.SandboxId) is not { } secret)
            {
                return BrokerSubstitution.Refused("A placeholder in this request stands for no secret this sandbox may use now.");
            }

            if (!SandboxSecretRules.Covers(secret.Hosts, host))
            {
                logger.LogWarning("Sandbox {SandboxId} tried to send secret {SecretId} to a host it is not bound to", session.SandboxId, secret.Id);
                return BrokerSubstitution.Refused($"{secret.Name} may not be sent to {host}.");
            }

            if (placement is BrokerPlacement.Query && !secret.AllowInQuery || placement is BrokerPlacement.Body && !secret.AllowInBody)
            {
                return BrokerSubstitution.Refused($"{secret.Name} may only be sent in a request header.");
            }

            values[placeholder] = await vault.Reveal(secret.SecretId, cancellationToken);
            used.Add(secret);
        }

        await RecordUses(services, session, host, used, cancellationToken);
        return new(values, null);
    }

    /// <summary>
    /// Of the secrets a placeholder stands for, the one that applies now: a bot's own, else the project's whose command
    /// is running, else the space's.
    /// </summary>
    private SandboxSecret? Pick(List<SandboxSecret> candidates, Guid sandboxId)
    {
        if (candidates.FirstOrDefault(secret => secret.Scope is SandboxSecretScope.Bots) is { } forBot)
        {
            return forBot;
        }

        var project = projects.ProjectOf(sandboxId);
        return candidates.FirstOrDefault(secret => secret.Scope is SandboxSecretScope.Project && secret.ProjectId == project)
            ?? candidates.FirstOrDefault(secret => secret.Scope is SandboxSecretScope.Space);
    }

    private async Task RecordUses(IServiceProvider services, BrokerSession session, string host, List<SandboxSecret> used, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var brokers = services.GetRequiredService<CredentialBrokerRepository>();
        var audit = services.GetRequiredService<AuditRepository>();

        foreach (var secret in used)
        {
            await brokers.MarkUsed(secret.Id, now, cancellationToken);

            if (recordedUses.TryGetValue((session.SandboxId, secret.Id), out var last) && now - last < useRecordedEvery)
            {
                continue;
            }

            recordedUses[(session.SandboxId, secret.Id)] = now;
            audit.Add(new AuditLogEntry
            {
                ActorKind = ActorKind.Bot,
                ActorId = session.OwnerBotId,
                Action = "sandbox_secret.used",
                TargetKind = AuditTargets.SandboxSecret,
                TargetId = secret.Id,
                Data = JsonSerializer.SerializeToElement(new { name = secret.Name, sandbox_id = session.SandboxId, host }),
                CreatedAt = now,
            });
        }
    }

    #endregion
}
