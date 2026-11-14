namespace Drover.Adapters.E2B;

/// <summary>
/// Sandboxes at E2B, through Drover's own client for E2B's published REST API and in-sandbox daemon. Every operation on
/// an existing sandbox first reads the provider's record and refuses unless its <c>drover.space</c> and
/// <c>drover.group</c> metadata match the handle: the control plane needs only the API key and an id, so this check,
/// not the access token, is what keeps one group's handle off another group's sandbox there.
/// </summary>
public sealed partial class E2BSandboxProvider(
    IHttpClientFactory httpClients,
    IOptions<E2BOptions> options,
    ILogger<E2BSandboxProvider> logger) : ISandboxProvider
{
    #region Private Fields

    public const string ProviderKind = "e2b";

    public const string ApiClientName = "drover-e2b-api";

    public const string EnvdClientName = "drover-e2b-envd";

    public const string SpaceKey = "drover.space";

    public const string GroupKey = "drover.group";

    private readonly ControlPlaneClient api = new(httpClients, options.Value, logger);

    private readonly EnvdClient envd = new(httpClients, options.Value, logger);

    #endregion

    #region Properties

    public string Kind => ProviderKind;

    #endregion

    #region Interface Implementations

    public async Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken)
    {
        var request = new NewSandboxRequest(
            spec.Image ?? options.Value.Template,
            Seconds(spec.IdleTimeout),
            AutoPause: true,
            AutoPauseMemory: true,
            Metadata(spec.SpaceId, spec.SharingGroupId),
            spec.Environment.Count > 0 ? spec.Environment : null,
            NetworkRules.ForCreate(spec.Network));

        var created = await api.Create(request, spec.IdempotencyKey.ToString("D"), cancellationToken);

        if (!IsSandboxId(created.SandboxId))
        {
            throw FailureMapping.Malformed();
        }

        if (string.IsNullOrEmpty(created.EnvdAccessToken))
        {
            logger.LogWarning("Sandbox {SandboxId} came without an access token and is killed", created.SandboxId);
            await api.Kill(created.SandboxId, CancellationToken.None);
            throw FailureMapping.Malformed();
        }

        logger.LogInformation("Created sandbox {SandboxId} for sharing group {SharingGroupId}", created.SandboxId, spec.SharingGroupId);

        return new SandboxHandle(created.SandboxId, spec.SpaceId, spec.SharingGroupId, created.EnvdAccessToken);
    }

    public async Task<SandboxInfo> Describe(SandboxHandle sandbox, CancellationToken cancellationToken) =>
        await Find(sandbox, cancellationToken) is { } detail
            ? new SandboxInfo(
                StateOf(detail),
                detail.Metadata ?? new Dictionary<string, string>(),
                ProtoTime.Parse(detail.EndAt))
            : new SandboxInfo(ProviderSandboxState.Gone, new Dictionary<string, string>(), null);

    public async Task KeepAlive(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        await Check(sandbox, cancellationToken);
        await api.SetTimeout(sandbox.ProviderSandboxId, Seconds(idle), cancellationToken);
    }

    public async Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        if (StateOf(await Check(sandbox, cancellationToken)) == ProviderSandboxState.Running)
        {
            await api.Pause(sandbox.ProviderSandboxId, cancellationToken);
        }
    }

    public async Task<SandboxHandle> Resume(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        await Check(sandbox, cancellationToken);

        var resumed = await api.Connect(sandbox.ProviderSandboxId, Seconds(idle), cancellationToken);

        return string.IsNullOrEmpty(resumed.EnvdAccessToken) ? sandbox : sandbox with { AccessToken = resumed.EnvdAccessToken };
    }

    public async Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        if (await Find(sandbox, cancellationToken) is not null)
        {
            await api.Kill(sandbox.ProviderSandboxId, cancellationToken);
            logger.LogInformation("Destroyed sandbox {SandboxId}", sandbox.ProviderSandboxId);
        }
    }

    public async IAsyncEnumerable<CommandOutput> Run(
        SandboxHandle sandbox,
        CommandRequest command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await CheckRunning(sandbox, cancellationToken);

        await foreach (var output in envd.Start(sandbox, command, cancellationToken))
        {
            yield return output;
        }
    }

    public async Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken)
    {
        await CheckRunning(sandbox, cancellationToken);

        return await envd.ReadFile(sandbox, path, cancellationToken);
    }

    public async Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken)
    {
        await CheckRunning(sandbox, cancellationToken);
        await envd.WriteFile(sandbox, path, content, cancellationToken);
    }

    public async Task<IReadOnlyList<SandboxEntry>> ListFiles(SandboxHandle sandbox, string path, CancellationToken cancellationToken)
    {
        await CheckRunning(sandbox, cancellationToken);

        return await envd.ListDir(sandbox, path, cancellationToken);
    }

    public async Task SetNetworkPolicy(SandboxHandle sandbox, NetworkPolicy policy, CancellationToken cancellationToken)
    {
        var rules = NetworkRules.ForUpdate(policy);

        await Check(sandbox, cancellationToken);
        await api.UpdateNetwork(sandbox.ProviderSandboxId, rules, cancellationToken);
    }

    #endregion

    #region Metadata Check

    private async Task CheckRunning(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        if (StateOf(await Check(sandbox, cancellationToken)) != ProviderSandboxState.Running)
        {
            throw FailureMapping.Paused();
        }
    }

    private async Task<SandboxDetailReply> Check(SandboxHandle sandbox, CancellationToken cancellationToken) =>
        await Find(sandbox, cancellationToken)
        ?? throw FailureMapping.FromStatus((int)HttpStatusCode.NotFound, Plane.ControlPlane);

    /// <summary>
    /// The provider's record of the handle's sandbox, or null when it is gone. An id that is not E2B's shape is
    /// refused before it reaches a URL, because the id becomes part of the daemon's host name.
    /// </summary>
    private async Task<SandboxDetailReply?> Find(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        if (!IsSandboxId(sandbox.ProviderSandboxId))
        {
            throw FailureMapping.HandleMismatch();
        }

        var detail = await api.Get(sandbox.ProviderSandboxId, cancellationToken);

        if (detail is not null && !Matches(detail, sandbox))
        {
            logger.LogWarning(
                "Refused sandbox {SandboxId}: the provider's metadata does not match the handle for space {SpaceId}",
                sandbox.ProviderSandboxId,
                sandbox.SpaceId);
            throw FailureMapping.HandleMismatch();
        }

        return detail;
    }

    private static bool Matches(SandboxDetailReply detail, SandboxHandle sandbox) =>
        detail.SandboxId == sandbox.ProviderSandboxId
        && detail.Metadata is { } metadata
        && metadata.GetValueOrDefault(SpaceKey) == sandbox.SpaceId.ToString("D")
        && metadata.GetValueOrDefault(GroupKey) == sandbox.SharingGroupId.ToString("D");

    private static Dictionary<string, string> Metadata(Guid spaceId, Guid sharingGroupId) =>
        new() { [SpaceKey] = spaceId.ToString("D"), [GroupKey] = sharingGroupId.ToString("D") };

    #endregion

    #region Private Methods

    private static ProviderSandboxState StateOf(SandboxDetailReply detail) =>
        detail.State == "paused" ? ProviderSandboxState.Paused : ProviderSandboxState.Running;

    /// <summary>E2B's time-to-live is whole seconds, at least one.</summary>
    private static int Seconds(TimeSpan duration) =>
        (int)Math.Clamp(Math.Ceiling(duration.TotalSeconds), 1, int.MaxValue);

    /// <summary>E2B ids are lowercase letters and digits. The daemon's host label is <c>49983-{id}</c>, at most 63 characters.</summary>
    private static bool IsSandboxId(string? id) => id is not null && SandboxId().IsMatch(id);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,56}$")]
    private static partial Regex SandboxId();

    #endregion
}
