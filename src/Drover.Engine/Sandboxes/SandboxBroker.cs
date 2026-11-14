namespace Drover.Engine.Sandboxes;

/// <summary>What a command left behind: its exit code (none when it was stopped), and its output as the model reads it.</summary>
public sealed record SandboxCommandResult(int? ExitCode, string Stdout, string Stderr, bool Truncated, bool TimedOut, bool NewSandbox);

/// <summary>A file read from a sandbox. <see cref="Content"/> is null when the bytes are not UTF-8 text.</summary>
public sealed record SandboxFileResult(string? Content, long Size, bool NewSandbox);

/// <summary>The entries of a folder in a sandbox, at most a fixed number of them.</summary>
public sealed record SandboxListResult(IReadOnlyList<SandboxEntry> Entries, bool Truncated, bool NewSandbox);

/// <summary>A screenshot of a sandbox's screen, as a PNG file, and the size it says it is.</summary>
public sealed record SandboxScreenshot(byte[] Png, int Width, int Height, bool NewSandbox);


/// <summary>
/// The only caller of <see cref="ISandboxProvider"/>. For each sandbox tool call it works out which sandbox the bot
/// means and whether it may use it, finds or creates that sandbox's row and token in one short tenant transaction,
/// then calls the provider with no transaction open, and records the use in another short one. The access token goes
/// from the vault into the handle and nowhere else: output is scrubbed of it before it reaches an event or a result,
/// and no log, exception, or argument carries it.
/// </summary>
public sealed class SandboxBroker(
    ISandboxProvider provider,
    IOptions<DeploymentPolicyOptions> deployment,
    IOptions<RunOptions> runOptions,
    TimeProvider time,
    ILogger<SandboxBroker> logger)
{
    #region Private Fields

    /// <summary>How long a sandbox stays up past a command's own limit, so it cannot pause under a running command.</summary>
    private static readonly TimeSpan commandMargin = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan defaultIdle = TimeSpan.FromMinutes(15);

    private const int MaxEntries = 1000;

    /// <summary>The least time a sandbox stays up for a desktop action: starting the display can take a minute and a half.</summary>
    private static readonly TimeSpan desktopMargin = TimeSpan.FromMinutes(2);

    /// <summary>The network rules last set on each sandbox, so a change to a bot's settings reaches its sandbox on next use.</summary>
    private readonly ConcurrentDictionary<Guid, NetworkPolicy> appliedNetworks = new();

    #endregion

    #region Public Methods

    /// <summary>Runs a command, streaming its output into the run's events, and returns what it printed.</summary>
    public async Task<SandboxCommandResult> Run(
        BuiltinToolCall call,
        string? environment,
        string command,
        string? workingDirectory,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var limit = CommandLimit(timeout);
        var lease = await Acquire(call, environment, limit + commandMargin, screen: false, cancellationToken);

        // The model wrote the whole command, for its own sandbox; Drover adds no text of its own to it, so a shell
        // reading it can only do what the model could do anyway with a second call.
        var request = new CommandRequest
        {
            Program = "/bin/bash",
            Arguments = ["-l", "-c", command],
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            Timeout = limit,
        };
        var transcript = new CommandTranscript(lease.Handle.AccessToken, runOptions.Value.MaxToolResultCharacters / 2, call.Output);
        int? exitCode = null;
        var timedOut = false;

        try
        {
            await foreach (var output in provider.Run(lease.Handle, request, cancellationToken))
            {
                exitCode = await transcript.Take(output) ?? exitCode;
            }
        }
        catch (SandboxProviderException failure) when (failure.Failure is SandboxFailure.TimedOut)
        {
            logger.LogInformation("A command in sandbox {SandboxId} ran past its {Seconds} seconds and was stopped", lease.SandboxId, limit.TotalSeconds);
            timedOut = true;
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        await transcript.Finish();
        await Finish(call, lease, readsSandbox: true, cancellationToken);
        return new SandboxCommandResult(timedOut ? null : exitCode ?? 0, transcript.Stdout, transcript.Stderr, transcript.Truncated, timedOut, lease.NewSandbox);
    }

    public async Task<SandboxFileResult> ReadFile(BuiltinToolCall call, string? environment, string path, CancellationToken cancellationToken)
    {
        var lease = await Acquire(call, environment, null, screen: false, cancellationToken);
        var maxBytes = deployment.Value.Rules.MaxSandboxFileBytes;
        byte[] bytes;

        try
        {
            await using var content = await provider.ReadFile(lease.Handle, path, cancellationToken);
            bytes = await ReadAtMost(content, maxBytes, cancellationToken)
                ?? throw new BuiltinToolException($"{path} is larger than {maxBytes} bytes, the most this server reads from a sandbox file.");
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(call, lease, readsSandbox: true, cancellationToken);
        return new SandboxFileResult(ReadText(bytes, lease.Handle.AccessToken), bytes.Length, lease.NewSandbox);
    }

    public async Task<bool> WriteFile(BuiltinToolCall call, string? environment, string path, string content, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var maxBytes = deployment.Value.Rules.MaxSandboxFileBytes;

        if (bytes.Length > maxBytes)
        {
            throw new BuiltinToolException($"The content is {bytes.Length} bytes; this server writes at most {maxBytes} bytes to a sandbox file.");
        }

        var lease = await Acquire(call, environment, null, screen: false, cancellationToken);

        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            await provider.WriteFile(lease.Handle, path, stream, cancellationToken);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(call, lease, readsSandbox: false, cancellationToken);
        return lease.NewSandbox;
    }

    public async Task<SandboxListResult> ListFiles(BuiltinToolCall call, string? environment, string path, CancellationToken cancellationToken)
    {
        var lease = await Acquire(call, environment, null, screen: false, cancellationToken);
        IReadOnlyList<SandboxEntry> entries;

        try
        {
            entries = await provider.ListFiles(lease.Handle, path, cancellationToken);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(call, lease, readsSandbox: true, cancellationToken);
        return new SandboxListResult([.. entries.Take(MaxEntries)], entries.Count > MaxEntries, lease.NewSandbox);
    }

    #endregion

    #region Computer Use

    /// <summary>
    /// A screenshot of the bot's sandbox screen. What it shows is read from the sandbox like a file, so it taints the
    /// run when the sandbox can reach the network; no redaction can reach into its pixels.
    /// </summary>
    public async Task<SandboxScreenshot> Screenshot(BuiltinToolCall call, CancellationToken cancellationToken)
    {
        var lease = await AcquireScreen(call, cancellationToken);
        var path = $"/tmp/drover-screen-{Guid.NewGuid():N}.png";
        var maxBytes = deployment.Value.Rules.MaxSandboxFileBytes;
        byte[] png;

        await RunOnScreen(lease, DesktopCommands.Screenshot(path), "take the screenshot", cancellationToken);

        try
        {
            await using var content = await provider.ReadFile(lease.Handle, path, cancellationToken);
            png = await ReadAtMost(content, maxBytes, cancellationToken)
                ?? throw new BuiltinToolException($"The screenshot is larger than {maxBytes} bytes, the most this server reads from a sandbox.");
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }
        finally
        {
            await RemoveQuietly(lease, path);
        }

        var (width, height) = DesktopCommands.ReadPngSize(png) ?? throw new BuiltinToolException("The screenshot did not come out as a PNG image.");
        await Finish(call, lease, readsSandbox: true, cancellationToken);
        return new SandboxScreenshot(png, width, height, lease.NewSandbox);
    }

    /// <summary>
    /// Runs one mouse or keyboard action on the bot's sandbox screen. The command is a program and its arguments, built
    /// by <see cref="DesktopCommands"/>; its output is not read, so nothing from the sandbox comes back with it.
    /// </summary>
    public async Task<bool> Act(BuiltinToolCall call, CommandRequest action, string doing, CancellationToken cancellationToken)
    {
        var lease = await AcquireScreen(call, cancellationToken);
        await RunOnScreen(lease, action, doing, cancellationToken);
        await Finish(call, lease, readsSandbox: false, cancellationToken);
        return lease.NewSandbox;
    }

    /// <summary>The bot's own sandbox, which must have a screen, with its display and desktop session running.</summary>
    private async Task<SandboxLease> AcquireScreen(BuiltinToolCall call, CancellationToken cancellationToken)
    {
        var lease = await Acquire(call, null, desktopMargin, screen: true, cancellationToken);

        if (await RunQuietly(lease, DesktopCommands.StartDisplay(), cancellationToken) is not 0)
        {
            throw new BuiltinToolException(
                "The sandbox's screen did not start. Try again in a moment; if it keeps failing, a person can turn the bot's Desktop setting off and on to get a new sandbox.");
        }

        return lease;
    }

    private async Task RunOnScreen(SandboxLease lease, CommandRequest command, string doing, CancellationToken cancellationToken)
    {
        if (await RunQuietly(lease, command, cancellationToken) is not 0)
        {
            throw new BuiltinToolException($"The sandbox could not {doing}.");
        }
    }

    /// <summary>Runs a command and returns its exit code, or -1 when it ran out of time. Its output is never read.</summary>
    private async Task<int> RunQuietly(SandboxLease lease, CommandRequest command, CancellationToken cancellationToken)
    {
        var exitCode = 0;

        try
        {
            await foreach (var output in provider.Run(lease.Handle, command, cancellationToken))
            {
                if (output is CommandExited exited)
                {
                    exitCode = exited.ExitCode;
                }
            }
        }
        catch (SandboxProviderException failure) when (failure.Failure is SandboxFailure.TimedOut)
        {
            logger.LogInformation("A desktop command in sandbox {SandboxId} ran past its time limit", lease.SandboxId);
            return -1;
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        return exitCode;
    }

    /// <summary>Removes a file Drover made in the sandbox. A failure is logged, not passed on: the file is in /tmp.</summary>
    private async Task RemoveQuietly(SandboxLease lease, string path)
    {
        try
        {
            await RunQuietly(lease, DesktopCommands.Remove(path), CancellationToken.None);
        }
        catch (BuiltinToolException)
        {
            logger.LogInformation("A screenshot file in sandbox {SandboxId} was not removed", lease.SandboxId);
        }
    }

    #endregion

    #region Finding The Sandbox

    /// <summary>
    /// The sandbox the call means, running, for at least <paramref name="keepAtLeast"/>. A sandbox the provider no
    /// longer has is retired and replaced once, and the lease says so, because its files are gone.
    /// </summary>
    private async Task<SandboxLease> Acquire(
        BuiltinToolCall call,
        string? environment,
        TimeSpan? keepAtLeast,
        bool screen,
        CancellationToken cancellationToken)
    {
        var target = await Locate(call, environment, cancellationToken);

        if (screen && !DesktopFor(target.Owner))
        {
            throw new BuiltinToolException("This bot's sandbox has no screen. A person can turn on Desktop in the bot's Sandbox settings.");
        }

        await DestroyRetired(call.Tenant, target, cancellationToken);
        var network = NetworkFor(target.Owner);
        var idle = IdleFor(target.Owner);
        var keepFor = keepAtLeast is { } longer && longer > idle ? longer : idle;
        var newSandbox = false;

        if (await Resume(call.Tenant, target, keepFor, cancellationToken) is not { } handle)
        {
            await Retire(call.Tenant, target.Row.Id, cancellationToken);
            target = await Locate(call, environment, cancellationToken);
            handle = await Resume(call.Tenant, target, keepFor, cancellationToken)
                ?? throw new BuiltinToolException("The sandbox provider lost the sandbox again right after it was made. Try again later.");
            newSandbox = true;
        }

        await ApplyNetwork(target.Row.Id, handle, network, cancellationToken);
        return new SandboxLease(target.Row.Id, handle, network, idle, keepFor, newSandbox);
    }

    /// <summary>The caller's group or the one it names, the access check, and the owner's live row, in one transaction.</summary>
    private async Task<SandboxTarget> Locate(BuiltinToolCall call, string? environment, CancellationToken cancellationToken)
    {
        try
        {
            return await call.Tenant.Transact(services => LocateIn(services, call, environment, cancellationToken), cancellationToken);
        }
        catch (Exception raced) when (SandboxRepository.IsAlreadyLive(raced))
        {
            // Another call made the owner's first sandbox row at the same moment. Its row is the one to use.
            logger.LogDebug("Bot {BotId} met another call creating the same sandbox; using that one", call.BotId);
            return await call.Tenant.Transact(services => LocateIn(services, call, environment, cancellationToken), cancellationToken);
        }
    }

    private async Task<SandboxTarget> LocateIn(IServiceProvider services, BuiltinToolCall call, string? environment, CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();
        var caller = await bots.FindBot(call.BotId, cancellationToken);

        if (caller is null or { Status: BotStatus.Removed } || caller.SandboxPolicy is SandboxPolicy.None)
        {
            throw new BuiltinToolException("This bot has no sandbox. A person can give it one in the bot's Sandbox settings.");
        }

        var callerOwner = SandboxGroups.OwnerOf(caller.Id, await bots.ListSandboxPath(caller.Id, cancellationToken));
        var owner = string.IsNullOrWhiteSpace(environment)
            ? await FindBot(bots, callerOwner, cancellationToken)
            : await FindNamedOwner(bots, caller, callerOwner, environment, cancellationToken);
        var sandboxes = services.GetRequiredService<SandboxRepository>();
        var row = await sandboxes.FindLiveForOwner(owner.Id, cancellationToken);

        if (row is null)
        {
            row = new Sandbox
            {
                OwnerBotId = owner.Id,
                Policy = owner.SandboxPolicy,
                Provider = provider.Kind,
                ProviderSandboxId = string.Empty,
                State = SandboxState.Creating,
            };
            sandboxes.Add(row);
        }

        var vault = services.GetRequiredService<ISecretVault>();
        var token = row.AccessSecretId is { } secretId ? await vault.Reveal(secretId, cancellationToken) : null;
        List<RetiredSandbox> retired = [];

        foreach (var due in await sandboxes.ListDueForOwner(owner.Id, time.GetUtcNow(), cancellationToken))
        {
            var retiredToken = due.AccessSecretId is { } dueSecretId ? await vault.Reveal(dueSecretId, cancellationToken) : null;
            retired.Add(new RetiredSandbox(due.Id, due.ProviderSandboxId, retiredToken));
        }

        return new SandboxTarget(call.Tenant.SpaceId, owner, row, token, retired);
    }

    /// <summary>The owner of the sandbox another bot's handle names, when the caller may work in it.</summary>
    private static async Task<Bot> FindNamedOwner(BotRepository bots, Bot caller, Guid callerOwner, string environment, CancellationToken cancellationToken)
    {
        var handle = environment.Trim().TrimStart('@');
        var named = await bots.FindBotByHandle(handle, cancellationToken)
            ?? throw new BuiltinToolException($"There is no bot @{handle} in this space.");
        var ownerId = SandboxGroups.OwnerOf(named.Id, await bots.ListSandboxPath(named.Id, cancellationToken));
        var owner = await FindBot(bots, ownerId, cancellationToken);
        var shares = owner.SandboxPolicy is SandboxPolicy.Named ? await bots.ListSandboxShares(owner.Id, cancellationToken) : [];

        return SandboxGroups.MayEnter(caller.Id, callerOwner, owner.Id, owner.SandboxPolicy, shares)
            ? owner
            : throw new BuiltinToolException($"This bot may not work in the sandbox of @{handle}. Only bots that share it can.");
    }

    private static async Task<Bot> FindBot(BotRepository bots, Guid botId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed } bot
            ? bot
            : throw new BuiltinToolException("The bot that owns this sandbox is gone.");

    #endregion

    #region Provider Lifecycle

    /// <summary>
    /// A handle to the target's sandbox, running for <paramref name="keepFor"/>: created on first use, resumed (or kept)
    /// otherwise. Null when the provider no longer has the sandbox.
    /// </summary>
    private async Task<SandboxHandle?> Resume(RunTenant tenant, SandboxTarget target, TimeSpan keepFor, CancellationToken cancellationToken)
    {
        if (target.Token is null || target.Row.State is SandboxState.Creating)
        {
            return await Create(tenant, target, keepFor, cancellationToken);
        }

        var stored = new SandboxHandle(target.Row.ProviderSandboxId, target.SpaceId, target.Owner.Id, target.Token);
        SandboxHandle resumed;

        try
        {
            // Resuming a running sandbox only keeps it for longer, so this one call covers both. The provider pauses a
            // sandbox on its own once its time runs out, so the row cannot say which one it is.
            resumed = await provider.Resume(stored, keepFor, cancellationToken);
        }
        catch (SandboxProviderException failure) when (failure.Failure is SandboxFailure.NotFound)
        {
            logger.LogInformation("Sandbox {SandboxId} of bot {BotId} is gone at the provider; it is replaced", target.Row.Id, target.Owner.Id);
            return null;
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        if (resumed.AccessToken != stored.AccessToken)
        {
            await tenant.Transact(
                services => services.GetRequiredService<ISecretVault>().Replace(target.Row.AccessSecretId!.Value, resumed.AccessToken, cancellationToken),
                cancellationToken);
        }

        return resumed;
    }

    /// <summary>
    /// Creates the provider sandbox for a new row. The row's id is the idempotency key, so a retry after a failure, or
    /// two calls that met on the same new row, make one sandbox. The token goes into the vault as a space secret.
    /// </summary>
    private async Task<SandboxHandle> Create(RunTenant tenant, SandboxTarget target, TimeSpan keepFor, CancellationToken cancellationToken)
    {
        var network = NetworkFor(target.Owner);
        var desktop = DesktopFor(target.Owner);
        SandboxHandle created;

        try
        {
            created = await provider.Create(
                new SandboxSpec
                {
                    SpaceId = target.SpaceId,
                    SharingGroupId = target.Owner.Id,
                    IdempotencyKey = target.Row.Id,
                    Network = network,
                    IdleTimeout = keepFor,
                    Desktop = desktop,
                    Environment = desktop ? DesktopCommands.Environment : new Dictionary<string, string>(),
                },
                cancellationToken);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        await tenant.Transact(
            async services =>
            {
                var row = await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(target.Row.Id, CancellationToken.None)
                    ?? throw new BuiltinToolException("The sandbox was removed while it was being made.");
                var vault = services.GetRequiredService<ISecretVault>();

                if (row.AccessSecretId is { } secretId)
                {
                    await vault.Replace(secretId, created.AccessToken, CancellationToken.None);
                }
                else
                {
                    row.AccessSecretId = await vault.Store(SecretKind.SandboxToken, created.AccessToken, null, CancellationToken.None);
                }

                row.ProviderSandboxId = created.ProviderSandboxId;
                MarkRunning(row);
            },
            CancellationToken.None);
        appliedNetworks[target.Row.Id] = network;
        logger.LogInformation("Created sandbox {SandboxId} for bot {BotId} at {Provider}", target.Row.Id, target.Owner.Id, provider.Kind);
        return created;
    }

    /// <summary>Marks a sandbox the provider no longer has as destroyed and forgets its token.</summary>
    private async Task Retire(RunTenant tenant, Guid sandboxId, CancellationToken cancellationToken)
    {
        appliedNetworks.TryRemove(sandboxId, out _);
        await tenant.Transact(
            async services =>
            {
                if (await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(sandboxId, cancellationToken) is not { } row)
                {
                    return;
                }

                if (row.AccessSecretId is { } secretId)
                {
                    await services.GetRequiredService<ISecretVault>().Delete(secretId, cancellationToken);
                    row.AccessSecretId = null;
                }

                row.State = SandboxState.Destroyed;
            },
            cancellationToken);
    }

    /// <summary>
    /// Destroys the owner's sandboxes that were taken out of use and are due, such as the one a bot had before its
    /// Desktop setting changed. One the provider cannot destroy now is tried again on the next use.
    /// </summary>
    private async Task DestroyRetired(RunTenant tenant, SandboxTarget target, CancellationToken cancellationToken)
    {
        foreach (var retired in target.Retired)
        {
            if (retired.ProviderSandboxId.Length > 0)
            {
                try
                {
                    await provider.Destroy(
                        new SandboxHandle(retired.ProviderSandboxId, target.SpaceId, target.Owner.Id, retired.Token ?? string.Empty),
                        cancellationToken);
                }
                catch (SandboxProviderException failure)
                {
                    logger.LogWarning("Retired sandbox {SandboxId} could not be destroyed yet: {Failure}", retired.Id, failure.Failure);
                    continue;
                }
            }

            await Retire(tenant, retired.Id, cancellationToken);
            logger.LogInformation("Destroyed retired sandbox {SandboxId} of bot {BotId}", retired.Id, target.Owner.Id);
        }
    }

    private async Task ApplyNetwork(Guid sandboxId, SandboxHandle handle, NetworkPolicy network, CancellationToken cancellationToken)
    {
        if (appliedNetworks.TryGetValue(sandboxId, out var applied) && SameNetwork(applied, network))
        {
            return;
        }

        try
        {
            await provider.SetNetworkPolicy(handle, network, cancellationToken);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        appliedNetworks[sandboxId] = network;
    }

    /// <summary>
    /// Records the use: the row is running and was used now, and the tool call names the sandbox. A sandbox kept up
    /// longer for a long command goes back to its idle time. Text from a sandbox that can reach the network taints the run.
    /// </summary>
    private async Task Finish(BuiltinToolCall call, SandboxLease lease, bool readsSandbox, CancellationToken cancellationToken)
    {
        if (lease.KeptFor > lease.Idle)
        {
            try
            {
                await provider.KeepAlive(lease.Handle, lease.Idle, cancellationToken);
            }
            catch (SandboxProviderException failure)
            {
                logger.LogWarning("Sandbox {SandboxId} stays up for its command's time limit: {Failure}", lease.SandboxId, failure.Failure);
            }
        }

        var now = time.GetUtcNow();
        await call.Tenant.Transact(
            async services =>
            {
                if (await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(lease.SandboxId, cancellationToken) is { } row)
                {
                    MarkRunning(row);
                    row.LastUsedAt = now;
                    row.ExpiresAt = now + lease.Idle;
                }

                await services.GetRequiredService<ToolCallRepository>().SetSandbox(call.ToolCallId, lease.SandboxId, cancellationToken);
            },
            cancellationToken);

        if (readsSandbox && lease.Network.Access is not NetworkAccess.Off)
        {
            await call.Taint();
        }
    }

    #endregion

    #region Limits

    /// <summary>The bot's network setting, unless the deployment forbids it. Limits only narrow, so that is a refusal.</summary>
    private NetworkPolicy NetworkFor(Bot owner) =>
        owner.SandboxSettings.Network is NetworkAccess.Open && !deployment.Value.Rules.AllowOpenSandboxNetwork
            ? throw new BuiltinToolException(
                $"This server does not let a sandbox reach the open internet ({nameof(DeploymentPolicy.AllowOpenSandboxNetwork)}). In the bot's Sandbox settings, choose Off or Named hosts.")
            : owner.SandboxSettings.ToNetworkPolicy();

    /// <summary>Whether the owner's sandbox has a screen. A bot set to have one where the deployment forbids it is refused.</summary>
    private bool DesktopFor(Bot owner) =>
        owner.SandboxSettings.Desktop && !deployment.Value.Rules.AllowDesktopSandboxes
            ? throw new BuiltinToolException(
                $"This server does not offer sandboxes with a screen ({nameof(DeploymentPolicy.AllowDesktopSandboxes)}). In the bot's Sandbox settings, turn Desktop off.")
            : owner.SandboxSettings.Desktop;

    /// <summary>The bot's idle time, or 15 minutes, never more than the deployment allows.</summary>
    private TimeSpan IdleFor(Bot owner)
    {
        var most = TimeSpan.FromMinutes(deployment.Value.Rules.MaxSandboxIdleMinutes);
        var asked = owner.SandboxSettings.IdleTimeoutMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : defaultIdle;
        return asked < most ? asked : most;
    }

    private TimeSpan CommandLimit(TimeSpan? asked)
    {
        var most = TimeSpan.FromSeconds(deployment.Value.Rules.MaxSandboxCommandSeconds);
        return asked is { } wanted && wanted > TimeSpan.Zero && wanted < most ? wanted : most;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Marks the row running, unless it was taken out of use while the call ran: its bot removed, or the sweep's
    /// cleanup. Such a row stays retiring, so the sweep pauses and destroys what the call made or used. One already
    /// destroyed becomes retiring again, due now, because a sandbox made after that holds an id the sweep must clear.
    /// </summary>
    private void MarkRunning(Sandbox row)
    {
        switch (row.State)
        {
            case SandboxState.Retiring:
                return;
            case SandboxState.Destroyed:
                row.State = SandboxState.Retiring;
                row.RetireAfter = time.GetUtcNow();
                return;
            default:
                row.State = SandboxState.Running;
                return;
        }
    }

    private static bool SameNetwork(NetworkPolicy left, NetworkPolicy right) =>
        left.Access == right.Access && left.AllowedHosts.SequenceEqual(right.AllowedHosts, StringComparer.Ordinal);

    /// <summary>The content, or null when it is longer than <paramref name="maxBytes"/>.</summary>
    private static async Task<byte[]?> ReadAtMost(Stream content, int maxBytes, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81_920];
        int read;

        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (copy.Length + read > maxBytes)
            {
                return null;
            }

            await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return copy.ToArray();
    }

    /// <summary>The bytes as text, scrubbed of the token, or null when they are not UTF-8 text.</summary>
    private static string? ReadText(byte[] bytes, string token) =>
        System.Text.Unicode.Utf8.IsValid(bytes) && !bytes.Contains((byte)0)
            ? CommandTranscript.Scrub(Encoding.UTF8.GetString(bytes), token)
            : null;

    /// <summary>A provider failure in plain words for the model. The provider's own message is never passed on.</summary>
    private BuiltinToolException Refusal(SandboxProviderException failure, string? path = null)
    {
        logger.LogWarning("A sandbox call failed: {Failure}", failure.Failure);
        return new BuiltinToolException(failure.Failure switch
        {
            SandboxFailure.NotFound when path is not null => $"There is no file or folder at {path} in the sandbox.",
            SandboxFailure.NotFound => "The sandbox is gone at the provider. Try again to start a new one.",
            SandboxFailure.HandleMismatch or SandboxFailure.Unauthorized =>
                "The provider's record of this sandbox does not match this bot, so nothing was done. The server's operator can find the details in its log.",
            SandboxFailure.TooLarge => "That is more than the sandbox accepts.",
            SandboxFailure.QuotaExceeded => "The server's sandbox account is at its limit. Tell the server's operator.",
            SandboxFailure.InvalidKey => "The server's sandbox provider key does not work. Tell the server's operator.",
            SandboxFailure.Rejected => "The sandbox provider refused this request.",
            SandboxFailure.TimedOut => "The sandbox did not answer in time.",
            _ => "The sandbox provider is not available right now. Try again in a moment.",
        });
    }

    #endregion

    #region Nested Types

    /// <summary>The bot whose sandbox a call uses, its row (new and unsaved, or stored), and its token if it has one.</summary>
    private sealed record SandboxTarget(Guid SpaceId, Bot Owner, Sandbox Row, string? Token, IReadOnlyList<RetiredSandbox> Retired);

    /// <summary>A sandbox taken out of use and due to be destroyed, with its token if it still has one.</summary>
    private sealed record RetiredSandbox(Guid Id, string ProviderSandboxId, string? Token);

    /// <summary>A sandbox ready for one operation, and what to record when the operation is done.</summary>
    private sealed record SandboxLease(Guid SandboxId, SandboxHandle Handle, NetworkPolicy Network, TimeSpan Idle, TimeSpan KeptFor, bool NewSandbox);

    #endregion
}
