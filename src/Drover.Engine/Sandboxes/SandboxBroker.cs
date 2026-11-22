namespace Drover.Engine.Sandboxes;

/// <summary>What a command left behind: its exit code (none when it was stopped), and its output as the model reads it.</summary>
public sealed record SandboxCommandResult(int? ExitCode, string Stdout, string Stderr, bool Truncated, bool TimedOut, bool NewSandbox);

/// <summary>A file read from a sandbox. <see cref="Content"/> is null when the bytes are not UTF-8 text.</summary>
public sealed record SandboxFileResult(string? Content, long Size, bool NewSandbox);

/// <summary>The entries of a folder in a sandbox, at most a fixed number of them.</summary>
public sealed record SandboxListResult(IReadOnlyList<SandboxEntry> Entries, bool Truncated, bool NewSandbox);

/// <summary>A screenshot of a sandbox's screen, as a PNG file, and the size it says it is.</summary>
public sealed record SandboxScreenshot(byte[] Png, int Width, int Height, bool NewSandbox);


/// <summary>How an attempt to let go of a held mouse button on a sandbox's screen ended.</summary>
public enum ReleaseOutcome
{
    /// <summary>The button was let go of.</summary>
    Released,

    /// <summary>The sandbox is out of use or destroyed, so no button on it can be held by anything that
    /// matters.</summary>
    Gone,

    /// <summary>
    /// The sandbox could not be reached to let go: its row says it is paused or expired, or has no token.
    /// </summary>
    Unreachable,
}


/// <summary>
/// Who is using a bot's sandbox: the bot itself, in a tool call of a run (<see cref="Call"/> is set), or a person
/// acting on the bot's own sandbox from the sandbox screen (it is not). <see cref="Tenant"/> opens the short
/// transactions, as the run's person or as the person acting.
/// </summary>
public sealed record SandboxCaller(RunTenant Tenant, Guid BotId, BuiltinToolCall? Call = null)
{
    public static SandboxCaller Of(BuiltinToolCall call) => new(call.Tenant, call.BotId, call);
}


/// <summary>
/// The only caller of <see cref="ISandboxProvider"/>, for a bot's tool calls and for a person acting on a bot's own
/// sandbox alike, so the same limits bind both. For each sandbox tool call it works out which sandbox the bot
/// means and whether it may use it, finds or creates that sandbox's row and token in one short tenant transaction,
/// then calls the provider with no transaction open, and records the use in another short one. The access token goes
/// from the vault into the handle and nowhere else: output is scrubbed of it before it reaches an event or a result,
/// and no log, exception, or argument carries it.
/// </summary>
public sealed class SandboxBroker(
    ISandboxProvider provider,
    IOptions<DeploymentPolicyOptions> deployment,
    IOptions<RunOptions> runOptions,
    SandboxCommandLocks commandLocks,
    RunScopes scopes,
    TimeProvider time,
    ILogger<SandboxBroker> logger)
{
    #region Private Fields

    /// <summary>How long a sandbox stays up past a command's own limit, so it cannot pause under a running command.</summary>
    private static readonly TimeSpan commandMargin = TimeSpan.FromMinutes(1);

    private const int DefaultCommandSeconds = 60;

    private static readonly TimeSpan defaultIdle = TimeSpan.FromMinutes(15);

    private const string HeldSentence =
        "A person has this sandbox's screen right now, because a bot that shares it asked them to take it over. Nothing was done in the sandbox. Try again once they hand it back; an owner or admin can see who holds it, and stop that run, on the bot's sandbox page.";

    private const string MovedSentence =
        "The bot's sandbox changed while this was under way, so nothing was done. Try again.";

    private const string HeldForBotWhat =
        "A mouse button a person pressed on this sandbox's screen could not be let go of, so nothing was done.";

    private const string HeldForBotNext =
        "Try again in a moment; an owner or admin can let go of it or rebuild the sandbox from the bot's sandbox page.";

    private const string HeldForBotSentence = $"{HeldForBotWhat} {HeldForBotNext}";

    private const string ClosedConversationSentence =
        "This conversation is private, and others in this space can reach this bot's sandbox, so it was not changed.";

    private const int MaxEntries = 1000;

    /// <summary>The least time a sandbox stays up for a desktop action: starting the display can take a minute and a half.</summary>
    private static readonly TimeSpan desktopMargin = TimeSpan.FromMinutes(2);

    /// <summary>The network rules last set on each sandbox, so a change to a bot's settings reaches its sandbox on next use.</summary>
    private readonly ConcurrentDictionary<Guid, NetworkPolicy> appliedNetworks = new();

    private SandboxPointerHolds? holds;

    #endregion

    #region Properties

    /// <summary>
    /// Runs after a bot write has passed its audience check and before the sandbox is acquired. A test holds the
    /// write open here so a narrowing can commit in between. Production leaves it unset.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeAcquire { get; set; }

    /// <summary>
    /// Runs when a person's screen action has found its sandbox and is about to wait for the sandbox's command lock. A
    /// test uses it to know the action has its sandbox before it waits. Not used outside tests.
    /// </summary>
    internal Action? LeaseHeld { get; set; }

    /// <summary>
    /// Runs as a person's pause, rebuild or turn off begins to look for the sandbox it acts on, inside the screen's
    /// gate, so a test can end the request during that lookup. Not used outside tests.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforePowerLookup { get; set; }

    /// <summary>
    /// The mouse buttons people hold down on each physical sandbox's screen, and the gate every person's input, every
    /// release and every power control on that screen takes turns on: see <see cref="SandboxPointerHolds"/>.
    /// </summary>
    internal SandboxPointerHolds Holds =>
        LazyInitializer.EnsureInitialized(ref holds, () => new SandboxPointerHolds(time, logger, Release));

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
        var caller = SandboxCaller.Of(call);
        await RefuseWriteFromAClosedConversation(caller, cancellationToken);
        return await RunCommand(caller, environment, command, workingDirectory, timeout, call.Output, cancellationToken);
    }

    private async Task<SandboxCommandResult> RunCommand(
        SandboxCaller caller,
        string? environment,
        string command,
        string? workingDirectory,
        TimeSpan? timeout,
        Func<ToolOutputChunk, Task> output,
        CancellationToken cancellationToken)
    {
        var limit = CommandLimit(timeout);
        var lease = await Acquire(caller, environment, limit + commandMargin, screen: false, cancellationToken);
        await AuthorizeLiveWrite(caller, cancellationToken);

        // The model wrote the whole command, for its own sandbox; Drover adds no text of its own to it, so a shell
        // reading it can only do what the model could do anyway with a second call.
        var request = new CommandRequest
        {
            Program = "/bin/bash",
            Arguments = ["-l", "-c", command],
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            Timeout = limit,
        };
        var transcript = new CommandTranscript(lease.Handle.AccessToken, runOptions.Value.MaxToolResultCharacters / 2, output);
        int? exitCode = null;
        var timedOut = false;

        await using (await HoldCommands(caller, lease.SandboxId, cancellationToken))
        {
            try
            {
                await foreach (var piece in provider.Run(lease.Handle, request, cancellationToken))
                {
                    exitCode = await transcript.Take(piece) ?? exitCode;
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
        }

        await transcript.Finish();
        await Finish(caller, lease, readsSandbox: true, cancellationToken);
        return new SandboxCommandResult(timedOut ? null : exitCode ?? 0, transcript.Stdout, transcript.Stderr, transcript.Truncated, timedOut, lease.NewSandbox);
    }

    public async Task<SandboxFileResult> ReadFile(BuiltinToolCall call, string? environment, string path, CancellationToken cancellationToken)
    {
        var lease = await Acquire(SandboxCaller.Of(call), environment, null, screen: false, cancellationToken);
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

        await Finish(SandboxCaller.Of(call), lease, readsSandbox: true, cancellationToken);
        return new SandboxFileResult(ReadText(bytes, lease.Handle.AccessToken), bytes.Length, lease.NewSandbox);
    }

    public async Task<bool> WriteFile(BuiltinToolCall call, string? environment, string path, string content, CancellationToken cancellationToken)
    {
        var caller = SandboxCaller.Of(call);
        await RefuseWriteFromAClosedConversation(caller, cancellationToken);
        var bytes = Encoding.UTF8.GetBytes(content);
        var maxBytes = deployment.Value.Rules.MaxSandboxFileBytes;

        if (bytes.Length > maxBytes)
        {
            throw new BuiltinToolException($"The content is {bytes.Length} bytes; this server writes at most {maxBytes} bytes to a sandbox file.");
        }

        var lease = await Acquire(caller, environment, null, screen: false, cancellationToken);
        await AuthorizeLiveWrite(caller, cancellationToken);

        try
        {
            await using (await HoldCommands(caller, lease.SandboxId, cancellationToken))
            {
                await using var stream = new MemoryStream(bytes, writable: false);
                await provider.WriteFile(lease.Handle, path, stream, cancellationToken);
            }
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(caller, lease, readsSandbox: false, cancellationToken);
        return lease.NewSandbox;
    }

    /// <summary>
    /// Puts a file a person attached into the bot's own sandbox, made on first use, for the bot to work on. Not a tool
    /// call: the server does it while it builds what the model reads, for a bot whose grants let it use a sandbox. The
    /// same file size limit binds it as a bot's own writes. The file's audience is read again under the space's copy
    /// fence, in the same transaction as the check that the sandbox is still there, and the provider is called only
    /// when everyone who can reach the sandbox can still see it. Refusals are <see cref="BuiltinToolException"/>.
    /// </summary>
    public async Task PutFile(SandboxCaller caller, string path, Stream content, long size, Guid fileId, CancellationToken cancellationToken)
    {
        var maxBytes = deployment.Value.Rules.MaxSandboxFileBytes;

        if (size > maxBytes)
        {
            throw new BuiltinToolException($"It is {size} bytes; this server writes at most {maxBytes} bytes to a sandbox file.");
        }

        var lease = await Acquire(caller, null, null, screen: false, cancellationToken);

        try
        {
            await using (await HoldCommands(caller, lease.SandboxId, cancellationToken))
            {
                await AuthorizeCopy(caller, lease, fileId, cancellationToken);
                await provider.WriteFile(lease.Handle, path, content, cancellationToken);
            }
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(caller, lease, readsSandbox: false, cancellationToken);
    }

    /// <summary>
    /// Hands the file at <paramref name="path"/>, and nothing else in the sandbox, to <paramref name="keep"/> as it is
    /// read, for a bot sharing it into its conversation. A file that holds the sandbox's access token is refused. It is
    /// a read of the sandbox like any other, so it taints the run as one would.
    /// </summary>
    public async Task<T> CopyOut<T>(
        BuiltinToolCall call,
        string? environment,
        string path,
        Func<Stream, Task<T>> keep,
        CancellationToken cancellationToken)
    {
        var caller = SandboxCaller.Of(call);
        var lease = await Acquire(caller, environment, null, screen: false, cancellationToken);
        T kept;

        try
        {
            await using var content = await provider.ReadFile(lease.Handle, path, cancellationToken);
            await using var guarded = new TokenGuardStream(content, lease.Handle.AccessToken);
            kept = await keep(guarded);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(caller, lease, readsSandbox: true, cancellationToken);
        return kept;
    }

    public async Task<SandboxListResult> ListFiles(BuiltinToolCall call, string? environment, string path, CancellationToken cancellationToken)
    {
        var lease = await Acquire(SandboxCaller.Of(call), environment, null, screen: false, cancellationToken);
        IReadOnlyList<SandboxEntry> entries;

        try
        {
            entries = await provider.ListFiles(lease.Handle, path, cancellationToken);
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure, path);
        }

        await Finish(SandboxCaller.Of(call), lease, readsSandbox: true, cancellationToken);
        return new SandboxListResult([.. entries.Take(MaxEntries)], entries.Count > MaxEntries, lease.NewSandbox);
    }

    #endregion

    #region Computer Use

    /// <summary>
    /// A screenshot of the bot's sandbox screen. What it shows is read from the sandbox like a file, so it taints the
    /// run when the sandbox can reach the network; no redaction can reach into its pixels.
    /// </summary>
    public Task<SandboxScreenshot> Screenshot(BuiltinToolCall call, CancellationToken cancellationToken) =>
        Screenshot(SandboxCaller.Of(call), wake: true, cancellationToken);

    /// <summary>
    /// A screenshot of the bot's sandbox screen. A passive refresh only uses a running sandbox and leaves its idle
    /// deadline and state alone.
    /// </summary>
    public async Task<SandboxScreenshot> Screenshot(SandboxCaller caller, bool wake, CancellationToken cancellationToken)
    {
        var lease = wake
            ? await AcquireScreen(caller, create: true, cancellationToken) ?? throw MadeSandboxMissing()
            : await FindRunningScreen(caller, cancellationToken);
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
        if (wake)
        {
            await Finish(caller, lease, readsSandbox: true, cancellationToken);
        }

        return new SandboxScreenshot(png, width, height, lease.NewSandbox);
    }

    /// <summary>
    /// Runs one mouse or keyboard action on the bot's sandbox screen. The command is a program and its arguments, built
    /// by <see cref="DesktopCommands"/>; its output is not read, so nothing from the sandbox comes back with it.
    /// </summary>
    public async Task<bool> Act(BuiltinToolCall call, CommandRequest action, string doing, CancellationToken cancellationToken)
    {
        var caller = SandboxCaller.Of(call);
        await RefuseWriteFromAClosedConversation(caller, cancellationToken);
        return await Act(caller, action, doing, create: true, cancellationToken) ?? throw MadeSandboxMissing();
    }

    /// <summary>
    /// One action on the screen for a bot. Null when <paramref name="create"/> is false and there is no sandbox yet;
    /// otherwise whether the sandbox is a new one.
    /// </summary>
    public async Task<bool?> Act(
        SandboxCaller caller,
        CommandRequest action,
        string doing,
        bool create,
        CancellationToken cancellationToken)
    {
        if (await AcquireScreen(caller, create, cancellationToken) is not { } lease)
        {
            return null;
        }

        await AuthorizeLiveWrite(caller, cancellationToken);
        await using (await HoldCommands(caller, lease.SandboxId, cancellationToken))
        {
            await RunOnScreen(lease, action, doing, cancellationToken);
        }

        await Finish(caller, lease, readsSandbox: false, cancellationToken);
        return lease.NewSandbox;
    }

    /// <summary>
    /// One action of a person on the bot's screen, never making a sandbox. Null when the bot has no sandbox yet. The
    /// sandbox is found first, by its row, and its command lock taken; then <paramref name="around"/> is handed the
    /// steps, which it runs under that physical sandbox's gate: acquiring the sandbox (bound to that row, so a sandbox
    /// replaced since is never acquired instead), the action with the use recorded, and letting go of buttons on the
    /// exact sandbox acquired. Nothing reaches the provider before <paramref name="around"/> has admitted the action.
    /// </summary>
    internal async Task<bool?> Act(
        SandboxCaller caller,
        CommandRequest action,
        string doing,
        Func<ScreenUse, Task> around,
        CancellationToken cancellationToken)
    {
        if (await Locate(caller, null, create: false, cancellationToken) is not { } target)
        {
            return null;
        }

        var sandboxId = target.Row.Id;
        SandboxLease? lease = null;
        LeaseHeld?.Invoke();

        await using (await HoldCommands(caller, sandboxId, cancellationToken))
        {
            await around(new ScreenUse(
                sandboxId,
                async () =>
                {
                    lease = await AcquireScreen(caller, create: false, cancellationToken, only: sandboxId)
                        ?? throw new BuiltinToolException(MovedSentence);
                    await AuthorizeLiveWrite(caller, cancellationToken);
                    return lease.Handle.ProviderSandboxId;
                },
                async () =>
                {
                    var acquired = lease ?? throw new InvalidOperationException("The sandbox is acquired first.");
                    await RunOnScreen(acquired, action, doing, cancellationToken);
                    await Finish(caller, acquired, readsSandbox: false, cancellationToken);
                },
                (buttons, _) => lease is { } acquired
                    ? LetGo(acquired, buttons)
                    : Task.FromResult(ReleaseOutcome.Unreachable)));
        }

        return lease?.NewSandbox ?? false;
    }

    /// <summary>
    /// Lets go of a mouse button a person pressed, on the physical sandbox it was pressed on: the row is read by its
    /// id in its space, never through a bot, so a bot that was removed or now shares another sandbox changes nothing,
    /// and a button pressed on one sandbox is never let go of on another. It makes no sandbox, starts no display, takes
    /// no command lock, and does not count as use, so it waits on nothing a bot is running, and no setting refuses it:
    /// letting go is not use. One deadline of <see cref="DesktopCommands.ReleaseLimit"/> covers the lookup and the
    /// provider's work. The answer is <see cref="ReleaseOutcome.Gone"/> when the row is missing, out of use or
    /// destroyed, or names another provider sandbox than the one pressed; <see cref="ReleaseOutcome.Unreachable"/>
    /// when it says the sandbox is paused or expired or has no token, and the button is still to be let go of. A
    /// failure of the command itself, or of the deadline, is thrown.
    /// </summary>
    internal async Task<ReleaseOutcome> Release(PointerPress held, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(DesktopCommands.ReleaseLimit, time);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var tenant = scopes.For(held.SpaceId, held.Owner.UserId);
        var (outcome, handle) = await tenant.Transact(
            services => FindPressed(services, held, bound.Token),
            bound.Token);

        if (handle is null)
        {
            return outcome;
        }

        var network = new NetworkPolicy(NetworkAccess.Off, []);
        var lease = new SandboxLease(held.SandboxId, handle, network, defaultIdle, defaultIdle, NewSandbox: false);
        await RunOnScreen(lease, DesktopCommands.Release([held.Button]), "let go of the mouse button", bound.Token);
        return ReleaseOutcome.Released;
    }

    /// <summary>The id of the sandbox the bot's input would reach now, or null when there is none to reach.</summary>
    public async Task<Guid?> CurrentSandbox(SandboxCaller person, CancellationToken cancellationToken)
    {
        try
        {
            return (await Locate(person, null, create: false, cancellationToken))?.Row.Id;
        }
        catch (BuiltinToolException)
        {
            logger.LogDebug("Bot {BotId} has no sandbox that input would reach", person.BotId);
            return null;
        }
    }

    /// <summary>
    /// Lets go of every held button as the server stops. Holds exist only once a person's input or power control
    /// asked for them, through <see cref="SandboxControlService"/>, which stops them as it is disposed.
    /// </summary>
    internal ValueTask StopHolds() => holds?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// The pressed sandbox's row, read by its id, and a handle to it when a release can be sent now; otherwise why
    /// not. Only a row that still names the provider sandbox that was pressed gets a handle.
    /// </summary>
    private async Task<(ReleaseOutcome Outcome, SandboxHandle? Handle)> FindPressed(
        IServiceProvider services,
        PointerPress held,
        CancellationToken cancellationToken)
    {
        var row = await services.GetRequiredService<SandboxRepository>().FindSandbox(held.SandboxId, cancellationToken);

        if (row is null or { State: SandboxState.Retiring or SandboxState.Destroyed }
            || (held.ProviderSandboxId.Length > 0 && row.ProviderSandboxId != held.ProviderSandboxId))
        {
            return (ReleaseOutcome.Gone, null);
        }

        if (row is not { State: SandboxState.Running, AccessSecretId: { } secretId }
            || row.ExpiresAt <= time.GetUtcNow())
        {
            return (ReleaseOutcome.Unreachable, null);
        }

        var token = await services.GetRequiredService<ISecretVault>().Reveal(secretId, cancellationToken);
        return (ReleaseOutcome.Released, new SandboxHandle(row.ProviderSandboxId, held.SpaceId, row.OwnerBotId, token));
    }

    /// <summary>
    /// Lets go of buttons on the sandbox a person's action acquired, through the handle it used, whatever its row
    /// says: the action may have resumed a sandbox whose row still reads as paused. One deadline of
    /// <see cref="DesktopCommands.ReleaseLimit"/>, and the request's own cancellation does not end it.
    /// </summary>
    private async Task<ReleaseOutcome> LetGo(SandboxLease lease, IReadOnlyList<MouseButton> buttons)
    {
        using var deadline = new CancellationTokenSource(DesktopCommands.ReleaseLimit, time);
        await RunOnScreen(lease, DesktopCommands.Release(buttons), "let go of the mouse button", deadline.Token);
        return ReleaseOutcome.Released;
    }

    /// <summary>
    /// The bot's own sandbox, which must have a screen, with its display and desktop session running. Null when
    /// <paramref name="create"/> is false and the bot has no sandbox yet. With <paramref name="only"/>, it is that
    /// sandbox or nothing is done.
    /// </summary>
    private async Task<SandboxLease?> AcquireScreen(
        SandboxCaller caller,
        bool create,
        CancellationToken cancellationToken,
        Guid? only = null)
    {
        if (await Prepare(caller, null, desktopMargin, screen: true, create, cancellationToken, only) is not { } lease)
        {
            return null;
        }

        await EnsureDisplay(lease, cancellationToken);
        return lease;
    }

    private async Task<SandboxLease> FindRunningScreen(SandboxCaller caller, CancellationToken cancellationToken)
    {
        if (await Locate(caller, null, create: false, cancellationToken) is not { Token: { } token, Row.State: SandboxState.Running } target
            || target.Row.ExpiresAt <= time.GetUtcNow())
        {
            throw new BuiltinToolException("The sandbox is not running. Start or resume it to see the screen.");
        }

        if (!DesktopFor(target.Owner))
        {
            throw new BuiltinToolException("This bot's sandbox has no screen. A person can turn on Desktop in the bot's Sandbox settings.");
        }

        var idle = IdleFor(target.Owner);
        var handle = new SandboxHandle(target.Row.ProviderSandboxId, target.SpaceId, target.Owner.Id, token);
        var lease = new SandboxLease(target.Row.Id, handle, NetworkFor(target.Owner), idle, idle, NewSandbox: false);
        await EnsureDisplay(lease, cancellationToken);
        return lease;
    }

    private async Task EnsureDisplay(SandboxLease lease, CancellationToken cancellationToken)
    {
        if (await RunQuietly(lease, DesktopCommands.StartDisplay(), cancellationToken) is not 0 and var exitCode)
        {
            // 3: the display never answered. 4: no desktop session came up. -1: the start ran out of time.
            logger.LogWarning("The screen of sandbox {SandboxId} did not start: its start script ended with {ExitCode}", lease.SandboxId, exitCode);
            throw new BuiltinToolException(
                "The sandbox's screen did not start. Try again in a moment; if it keeps failing, rebuild the sandbox to get a new one.");
        }
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

    #region A Person's Controls

    /// <summary>
    /// Runs a person's command in the bot's own sandbox, made on first use, and returns what it printed. Nothing
    /// streams anywhere: the person sees the result. The output is scrubbed of the access token like a bot's.
    /// </summary>
    public Task<SandboxCommandResult> Run(SandboxCaller person, string command, TimeSpan limit, CancellationToken cancellationToken) =>
        RunCommand(person, null, command, null, limit, _ => Task.CompletedTask, cancellationToken);

    /// <summary>
    /// Pauses the bot's sandbox, keeping its files. False when it has none to pause. With <paramref name="only"/>, it
    /// is that sandbox or nothing is done; <paramref name="begin"/> is called once that is checked, just before the
    /// provider is asked.
    /// </summary>
    public async Task<bool> Pause(
        SandboxCaller person,
        CancellationToken cancellationToken,
        Guid? only = null,
        Action? begin = null)
    {
        await LookingForPower(cancellationToken);
        var found = await Locate(person, null, create: false, cancellationToken);

        if (found is { } located)
        {
            RefuseWhenMoved(located, only);
        }

        if (found is not { Token: { } token } target || target.Row.State is SandboxState.Creating)
        {
            return false;
        }

        begin?.Invoke();

        try
        {
            await provider.Pause(new SandboxHandle(target.Row.ProviderSandboxId, target.SpaceId, target.Owner.Id, token), cancellationToken);
        }
        catch (SandboxProviderException failure) when (failure.Failure is SandboxFailure.NotFound)
        {
            // A sandbox the provider no longer has is gone in every state: no pause can bring it back.
            logger.LogInformation("Sandbox {SandboxId} of bot {BotId} was gone at the provider when a person paused it", target.Row.Id, target.Owner.Id);
            await Retire(person.Tenant, target.Row.Id, cancellationToken);
            return false;
        }
        catch (SandboxProviderException failure)
        {
            throw Refusal(failure);
        }

        // A running sandbox becomes paused; one already paused stays. A row retired or destroyed while the provider
        // worked is left alone, so a pause cannot bring back a sandbox taken out of use.
        return await person.Tenant.Transact(
            async services =>
            {
                if (await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(target.Row.Id, cancellationToken) is not { } row)
                {
                    return false;
                }

                if (row.State is SandboxState.Running)
                {
                    row.State = SandboxState.Paused;
                    row.ExpiresAt = time.GetUtcNow();
                }

                return row.State is SandboxState.Paused;
            },
            cancellationToken);
    }

    /// <summary>
    /// Resumes the bot's paused sandbox, or keeps a running one. False when it has none; nothing is made. With
    /// <paramref name="only"/>, it is that sandbox or nothing is done, and one the provider lost is not replaced here.
    /// </summary>
    public async Task<bool> Resume(SandboxCaller person, CancellationToken cancellationToken, Guid? only = null)
    {
        if (await Prepare(person, null, null, screen: false, create: false, cancellationToken, only) is not { } lease)
        {
            return false;
        }

        await Finish(person, lease, readsSandbox: false, cancellationToken);
        return true;
    }

    /// <summary>
    /// Replaces the bot's sandbox with a fresh one: the live one is taken out of use and destroyed at the provider, its
    /// token deleted, and a new one made for the same sharing group, as when the Desktop setting changes. Its files are
    /// gone. With <paramref name="only"/>, the live one must be that sandbox and still live when it is taken out of
    /// use, or nothing is done; <paramref name="begin"/> is called once that is checked. The bot's sandbox must still
    /// be shared the same way when the new one is made, and a fresh one another call made meanwhile is left as it is.
    /// </summary>
    public async Task Rebuild(
        SandboxCaller person,
        CancellationToken cancellationToken,
        Guid? only = null,
        Action? begin = null)
    {
        await LookingForPower(cancellationToken);
        var target = await Locate(person, null, create: false, cancellationToken);

        if (target is null && only is { } meant && meant != Guid.Empty)
        {
            throw new BuiltinToolException(MovedSentence);
        }

        if (target is not null)
        {
            RefuseWhenMoved(target, only);
            begin?.Invoke();
            appliedNetworks.TryRemove(target.Row.Id, out _);
            var now = time.GetUtcNow();
            var retired = await person.Tenant.Transact(
                services => services.GetRequiredService<SandboxRepository>().RetireLive(target.Row.Id, now,
                    cancellationToken),
                cancellationToken);

            if (!retired)
            {
                // Another call took it out of use first: this rebuild's view of it is stale, so it makes nothing.
                throw new BuiltinToolException(MovedSentence);
            }
        }

        var fresh = await Locate(person, null, create: true, cancellationToken) ?? throw MadeSandboxMissing();

        if (target is not null && fresh.Owner.Id != target.Owner.Id)
        {
            throw new BuiltinToolException(MovedSentence);
        }

        if (target is not null && fresh.Row.State is not SandboxState.Creating)
        {
            // Another call made the new one between the retirement and this lookup. It is already fresh, so it is not
            // touched here; only the old one is destroyed.
            await DestroyRetired(person.Tenant, fresh, cancellationToken);
            logger.LogInformation(
                "Sandbox {SandboxId} of bot {BotId} was already replaced during a rebuild",
                target.Row.Id,
                target.Owner.Id);
            return;
        }

        var lease = await PrepareLocated(person, fresh, null, screen: false, bound: false, cancellationToken);
        await Finish(person, lease, readsSandbox: false, cancellationToken);
    }

    /// <summary>
    /// Destroys the bot's sandbox now, at the provider and in Drover: its files are gone and its token deleted. The next
    /// use makes a new one. False when it has none. With <paramref name="only"/>, it is that sandbox or nothing is
    /// done; <paramref name="begin"/> is called once that is checked, just before the provider is asked.
    /// </summary>
    public async Task<bool> TurnOff(
        SandboxCaller person,
        CancellationToken cancellationToken,
        Guid? only = null,
        Action? begin = null)
    {
        await LookingForPower(cancellationToken);
        if (await Locate(person, null, create: false, cancellationToken) is not { } target)
        {
            return false;
        }

        RefuseWhenMoved(target, only);
        begin?.Invoke();

        if (target is { Token: { } token, Row.ProviderSandboxId.Length: > 0 })
        {
            try
            {
                await provider.Destroy(new SandboxHandle(target.Row.ProviderSandboxId, target.SpaceId, target.Owner.Id, token), cancellationToken);
            }
            catch (SandboxProviderException failure) when (failure.Failure is not SandboxFailure.NotFound)
            {
                throw Refusal(failure);
            }
        }

        await Retire(person.Tenant, target.Row.Id, cancellationToken);
        logger.LogInformation("A person turned off sandbox {SandboxId} of bot {BotId}", target.Row.Id, target.Owner.Id);
        return true;
    }

    #endregion

    #region Finding The Sandbox

    /// <summary>
    /// The sandbox the call means, running, for at least <paramref name="keepAtLeast"/>, made on first use. A sandbox
    /// the provider no longer has is retired and replaced once, and the lease says so, because its files are gone.
    /// </summary>
    private async Task<SandboxLease> Acquire(
        SandboxCaller caller,
        string? environment,
        TimeSpan? keepAtLeast,
        bool screen,
        CancellationToken cancellationToken) =>
        await Prepare(caller, environment, keepAtLeast, screen, create: true, cancellationToken) ?? throw MadeSandboxMissing();

    /// <summary>
    /// As <see cref="Acquire"/>, but when <paramref name="create"/> is false and the owner has no live sandbox, null,
    /// and nothing is made. With <paramref name="only"/>, the live sandbox must be that one, or nothing is done (an
    /// empty id means none was expected), and one the provider lost is not replaced.
    /// </summary>
    private async Task<SandboxLease?> Prepare(
        SandboxCaller caller,
        string? environment,
        TimeSpan? keepAtLeast,
        bool screen,
        bool create,
        CancellationToken cancellationToken,
        Guid? only = null)
    {
        if (await Locate(caller, environment, create, cancellationToken) is not { } target)
        {
            return null;
        }

        RefuseWhenMoved(target, only);
        return await PrepareLocated(
            caller,
            target,
            keepAtLeast,
            screen,
            bound: only is not null,
            cancellationToken,
            environment);
    }

    /// <summary>
    /// The located sandbox running and ready. One the provider lost is retired and, unless <paramref name="bound"/>
    /// to that one sandbox, replaced once.
    /// </summary>
    private async Task<SandboxLease> PrepareLocated(
        SandboxCaller caller,
        SandboxTarget target,
        TimeSpan? keepAtLeast,
        bool screen,
        bool bound,
        CancellationToken cancellationToken,
        string? environment = null)
    {
        if (screen && !DesktopFor(target.Owner))
        {
            throw new BuiltinToolException("This bot's sandbox has no screen. A person can turn on Desktop in the bot's Sandbox settings.");
        }

        await DestroyRetired(caller.Tenant, target, cancellationToken);
        var network = NetworkFor(target.Owner);
        var idle = IdleFor(target.Owner);
        var keepFor = keepAtLeast is { } longer && longer > idle ? longer : idle;
        var newSandbox = false;

        if (await Resume(caller.Tenant, target, keepFor, cancellationToken) is not { } handle)
        {
            await Retire(caller.Tenant, target.Row.Id, cancellationToken);

            if (bound)
            {
                throw new BuiltinToolException(MovedSentence);
            }

            target = await Locate(caller, environment, create: true, cancellationToken) ?? throw MadeSandboxMissing();
            handle = await Resume(caller.Tenant, target, keepFor, cancellationToken)
                ?? throw new BuiltinToolException("The sandbox provider lost the sandbox again right after it was made. Try again later.");
            newSandbox = true;
        }

        await ApplyNetwork(target.Row.Id, handle, network, cancellationToken);
        return new SandboxLease(target.Row.Id, handle, network, idle, keepFor, newSandbox);
    }

    /// <summary>
    /// The caller's group or the one it names, the access check, and the owner's live row, in one transaction. With
    /// <paramref name="create"/>, an owner with no live row gets a new one; without, the answer is null.
    /// </summary>
    private async Task<SandboxTarget?> Locate(SandboxCaller caller, string? environment, bool create, CancellationToken cancellationToken)
    {
        try
        {
            return await caller.Tenant.Transact(services => LocateIn(services, caller, environment, create, cancellationToken), cancellationToken);
        }
        catch (Exception raced) when (SandboxRepository.IsAlreadyLive(raced))
        {
            // Another call made the owner's first sandbox row at the same moment. Its row is the one to use.
            logger.LogDebug("Bot {BotId} met another call creating the same sandbox; using that one", caller.BotId);
            return await caller.Tenant.Transact(services => LocateIn(services, caller, environment, create, cancellationToken), cancellationToken);
        }
    }

    /// <summary>
    /// True while a run of a bot whose sandbox this owner's is waits for a person at its screen. The run rows are shared
    /// space-wide, so every run of every bot in the group sees the hold, whichever conversation asked for it.
    /// </summary>
    private static async Task<bool> IsHeldByAPerson(IServiceProvider services, Guid ownerId, CancellationToken cancellationToken)
    {
        var holders = await services.GetRequiredService<RunRepository>().ListBotsWaitingFor(RunReasons.AwaitingTakeover, cancellationToken);
        var bots = services.GetRequiredService<BotRepository>();

        foreach (var holder in holders)
        {
            if (SandboxGroups.OwnerOf(holder, await bots.ListSandboxPath(holder, cancellationToken)) == ownerId)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<SandboxTarget?> LocateIn(
        IServiceProvider services,
        SandboxCaller caller,
        string? environment,
        bool create,
        CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();
        var bot = await bots.FindBot(caller.BotId, cancellationToken);

        if (bot is null or { Status: BotStatus.Removed } || bot.SandboxPolicy is SandboxPolicy.None)
        {
            throw new BuiltinToolException("This bot has no sandbox. A person can give it one in the bot's Sandbox settings.");
        }

        var callerOwner = SandboxGroups.OwnerOf(bot.Id, await bots.ListSandboxPath(bot.Id, cancellationToken));
        var owner = string.IsNullOrWhiteSpace(environment)
            ? await FindBot(bots, callerOwner, cancellationToken)
            : await FindNamedOwner(bots, bot, callerOwner, environment, cancellationToken);
        // While a person holds this sandbox's screen no bot uses the sandbox at all: a command or a file could reach
        // the screen, read it, or watch what the person types as well as a computer tool could.
        if (caller.Call is not null && await IsHeldByAPerson(services, owner.Id, cancellationToken))
        {
            throw new BuiltinToolException(HeldSentence);
        }

        var sandboxes = services.GetRequiredService<SandboxRepository>();
        var row = await sandboxes.FindLiveForOwner(owner.Id, cancellationToken);

        if (row is null && !create)
        {
            return null;
        }

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

        return new SandboxTarget(caller.Tenant.SpaceId, owner, row, token, retired);
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

    private Task LookingForPower(CancellationToken cancellationToken) =>
        BeforePowerLookup?.Invoke(cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// A power control that was made ready for one sandbox does not act on another: if the bot's sandbox is not the one
    /// that was meant, nothing is done. The check is on the row the control is about to act on, not a separate lookup.
    /// An empty id means that none was found to gate, so finding one now is also a change.
    /// </summary>
    private static void RefuseWhenMoved(SandboxTarget target, Guid? only)
    {
        if (only is { } meant && target.Row.Id != meant)
        {
            throw new BuiltinToolException(MovedSentence);
        }
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
    /// Records the use: the row is running and was used now, and a bot's tool call names the sandbox. A sandbox kept up
    /// longer for a long command goes back to its idle time. Text read from a sandbox that can reach the network is
    /// recorded on the run as read from an untrusted source; the record changes no grant.
    /// </summary>
    private async Task Finish(SandboxCaller caller, SandboxLease lease, bool readsSandbox, CancellationToken cancellationToken)
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
        await caller.Tenant.Transact(
            async services =>
            {
                if (await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(lease.SandboxId, cancellationToken) is { } row)
                {
                    MarkRunning(row);
                    row.LastUsedAt = now;
                    row.ExpiresAt = now + lease.Idle;
                }

                if (caller.Call is { } call)
                {
                    await services.GetRequiredService<ToolCallRepository>().SetSandbox(call.ToolCallId, lease.SandboxId, cancellationToken);
                }
            },
            cancellationToken);

        // A person reading their bot's sandbox is in no run, so there is nothing to record.
        if (readsSandbox && lease.Network.Access is not NetworkAccess.Off && caller.Call is { } reading)
        {
            await reading.Taint();
        }
    }

    /// <summary>
    /// Refuses a bot's write into the sandbox from a conversation not everyone who can reach the sandbox can see, as
    /// the copy of an attached file is refused when its rows are read. The audience is read as it is now, in one short
    /// transaction that closes before the sandbox is touched, so a conversation made private, or a personal space that
    /// gained a member, stops the write from the next call. A person's own controls have no call and never pass through here.
    /// </summary>
    private async Task RefuseWriteFromAClosedConversation(SandboxCaller caller, CancellationToken cancellationToken)
    {
        if (caller.Call is not { ThreadId: { } threadId })
        {
            return;
        }

        var covered = await caller.Tenant.Transact(
            async services =>
            {
                var thread = await services.GetRequiredService<ThreadRepository>().FindThread(threadId, cancellationToken);
                return thread is not null
                    && await FileAbilityReader.AudienceCoversSandbox(
                        caller.Tenant.SpaceId,
                        thread.AudienceUserId,
                        services.GetRequiredService<AccountRepository>(),
                        cancellationToken);
            },
            cancellationToken);

        if (!covered)
        {
            throw new BuiltinToolException(ClosedConversationSentence);
        }

        if (BeforeAcquire is { } pause)
        {
            await pause(cancellationToken);
        }
    }

    /// <summary>
    /// Reads the conversation's audience again under the space's copy fence, after the sandbox is acquired and before
    /// the provider is called. Retirement of every live sandbox holds the same fence until the narrower audience is
    /// committed, so a write that passed the earlier check cannot land on a sandbox acquired after that retirement.
    /// A person's own controls have no call and are not refused here.
    /// </summary>
    private static async Task AuthorizeLiveWrite(SandboxCaller caller, CancellationToken cancellationToken)
    {
        if (caller.Call is not { ThreadId: { } threadId })
        {
            return;
        }

        await caller.Tenant.Transact(
            async services =>
            {
                await services.GetRequiredService<SandboxRepository>().LockCopies(cancellationToken);
                var thread = await services.GetRequiredService<ThreadRepository>().FindThread(threadId, cancellationToken);
                var covered = thread is not null
                    && await FileAbilityReader.AudienceCoversSandbox(
                        caller.Tenant.SpaceId,
                        thread.AudienceUserId,
                        services.GetRequiredService<AccountRepository>(),
                        cancellationToken);

                if (!covered)
                {
                    throw new BuiltinToolException(ClosedConversationSentence);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Re-reads who may see <paramref name="fileId"/>, under the space's copy fence, before the provider is called. A
    /// narrowing of that audience holds the same fence until the change is committed, so this either sees the
    /// narrower audience and writes nothing, or copies onto a sandbox that the narrowing then retires.
    /// </summary>
    private static async Task AuthorizeCopy(SandboxCaller caller, SandboxLease lease, Guid fileId, CancellationToken cancellationToken)
    {
        await caller.Tenant.Transact(
            async services =>
            {
                var sandboxes = services.GetRequiredService<SandboxRepository>();
                await sandboxes.LockCopies(cancellationToken);
                var file = await services.GetRequiredService<FileRepository>().FindFile(fileId, cancellationToken);
                var covered = file is not null
                    && await FileAbilityReader.AudienceCoversSandbox(
                        caller.Tenant.SpaceId,
                        file.AudienceUserId,
                        services.GetRequiredService<AccountRepository>(),
                        cancellationToken);

                if (!covered)
                {
                    throw new BuiltinToolException(ClosedConversationSentence);
                }

                if (await sandboxes.GetSandboxForUpdate(lease.SandboxId, cancellationToken) is null)
                {
                    throw new BuiltinToolException("The sandbox was removed while the file was on its way. Try again.");
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Takes the sandbox's command lock. The wait is bounded and costs no database session; the lock is held on one
    /// session, with no tenant transaction open, until the provider call returns. A write takes the same lock, so it
    /// cannot change the sandbox while a command is admitted. When too many commands hold or
    /// wait for sandboxes, this is refused with a sentence the model or the person can read.
    /// </summary>
    /// <summary>
    /// Takes the sandbox's command lock. For a bot's call, every mouse button a person still holds on that sandbox's
    /// screen is let go of first, under the screen's gate, and the call is refused if one cannot be: a bot never works
    /// on a display with a person's button down, whether the person handed the screen back, stopped, or is still on it.
    /// </summary>
    private async Task<SandboxCommandGate> HoldCommands(SandboxCaller caller, Guid sandboxId, CancellationToken cancellationToken)
    {
        SandboxCommandGate gate;

        try
        {
            gate = await commandLocks.Hold(caller.Tenant.SpaceId, sandboxId, cancellationToken);
        }
        catch (SandboxBusyException busy)
        {
            throw new BuiltinToolException(busy.Message);
        }

        if (caller.Call is null || holds is not { } held || !held.IsHeldOn(caller.Tenant.SpaceId, sandboxId))
        {
            return gate;
        }

        try
        {
            if (!await held.ReleaseAll(caller.Tenant.SpaceId, sandboxId, cancellationToken))
            {
                throw new BuiltinToolException(HeldForBotSentence);
            }

            return gate;
        }
        catch (Exception)
        {
            await gate.DisposeAsync();
            throw;
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
        var wanted = asked is { } limit && limit > TimeSpan.Zero ? limit : TimeSpan.FromSeconds(DefaultCommandSeconds);
        return wanted < most ? wanted : most;
    }

    #endregion

    #region Private Methods

    private static InvalidOperationException MadeSandboxMissing() => new("A sandbox that may be made is always found.");

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
        var wait = SandboxWaits.Words(failure.RetryAfter);

        return new BuiltinToolException(failure.Failure switch
        {
            SandboxFailure.NotFound when path is not null => $"There is no file or folder at {path} in the sandbox.",
            SandboxFailure.NotFound => "The sandbox is gone at the provider. Try again to start a new one.",
            SandboxFailure.NotRunning => "The sandbox paused before this could run. Try again.",
            SandboxFailure.Conflict => $"The sandbox is starting, pausing or resuming. Try again {wait}.",
            SandboxFailure.Forbidden => "The server's sandbox account is not allowed to do this. Tell the server's operator.",
            SandboxFailure.RateLimited => $"The sandbox provider is limiting requests. Try again {wait}.",
            SandboxFailure.RetryLater => $"The sandbox provider cannot do this right now. Try again {wait}.",
            SandboxFailure.Unauthorized =>
                "The sandbox refused Drover's access to it, so nothing was done. Turning the sandbox off and starting a new one clears this. The server's operator can find the details in its log.",
            SandboxFailure.HandleMismatch =>
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
