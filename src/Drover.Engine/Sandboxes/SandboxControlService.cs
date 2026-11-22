namespace Drover.Engine.Sandboxes;

/// <summary>Why a person's action on a bot's sandbox was not done.</summary>
public enum SandboxControlRefusal
{
    None,

    /// <summary>The person is not in the space, or the bot is not there.</summary>
    NotFound,

    /// <summary>The person is in the space but may not change its bots.</summary>
    NotPermitted,

    /// <summary>The request itself is wrong: an unknown key, a point off the screen, an empty command.</summary>
    Invalid,

    /// <summary>The server, the deployment's rules, the bot's settings, or the provider refused; the words say which.</summary>
    Refused,
}


/// <summary>The outcome of a person's action on a bot's sandbox: the value, or why there is none, in plain words.</summary>
public sealed record SandboxControlResult<T>(T? Value, SandboxControlRefusal Refusal = SandboxControlRefusal.None, string? Problem = null);


/// <summary>
/// A person acting on a bot's own sandbox from the sandbox screen: its state, the power controls (pause, resume,
/// rebuild, turn off), the screen, the mouse and keyboard, and a terminal. Only a person who may change the bot (an
/// owner or admin of its space) may do any of it. Every provider call goes through <see cref="SandboxBroker"/>, so
/// every limit that binds the bot binds the person, and no tenant transaction is open across one. Every attempt that
/// reaches the broker writes one audit row with how it ended, in its own transaction that the request cannot cancel:
/// never the text typed, the command, or a picture.
/// </summary>
public sealed class SandboxControlService(
    IEnumerable<SandboxBroker> brokers,
    RunScopes scopes,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<SandboxControlService> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string NoProvider = "No sandbox provider is set up on this server. The server's operator adds one.";

    private const string NotPausedWhileHeld =
        "A mouse button on this screen could not be let go of, so it was not paused. Try again or rebuild it.";

    private const string NoSandboxYet = "This bot has no sandbox yet. Run a command in the terminal to start one.";

    /// <summary>The longest a person's command runs, below the deployment's own limit: the request waits for it.</summary>
    private static readonly TimeSpan commandLimit = TimeSpan.FromMinutes(5);

    private readonly SandboxBroker? broker = brokers.SingleOrDefault();

    private SandboxPointerHolds? heldButtons;

    /// <summary>The screenshot being taken of each bot's sandbox, so requests that meet share one provider call.</summary>
    private readonly ConcurrentDictionary<(Guid SpaceId, Guid BotId, bool Wake), Lazy<Task<SandboxScreenshot>>> screens = new();

    #endregion

    #region Properties

    public const int MaxCommandCharacters = FieldLimits.SandboxCommand;

    /// <summary>
    /// Runs when a screen request has joined the shared screenshot, taken or being taken, and before it waits for it.
    /// A test uses it to know every request has met before it lets the provider answer. Not used outside tests.
    /// </summary>
    internal Action? ScreenRequested { get; set; }

    /// <summary>
    /// Runs after the broker's work and before the audit row, so a test can end the request at that point. Not used
    /// outside tests.
    /// </summary>
    internal Action? WorkCompleted { get; set; }

    /// <summary>The mouse buttons people hold down, which are always let go of: see <see
    /// cref="SandboxPointerHolds"/>.</summary>
    internal SandboxPointerHolds Holds =>
        LazyInitializer.EnsureInitialized(ref heldButtons, () => new SandboxPointerHolds(time, logger, ReleaseHeld));

    #endregion

    #region Public Methods

    public async Task<SandboxControlResult<BotSandboxStatus>> GetStatus(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        await scopes.For(spaceId, userId).Transact(
            async services => await Check(services, spaceId, userId, botId, cancellationToken) is { } refusal
                ? Refuse<BotSandboxStatus>(refusal)
                : new(await ReadStatus(services, userId, botId, cancellationToken)),
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Pause(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(
            spaceId,
            userId,
            botId,
            "sandbox.paused",
            (sandbox, person, only) => sandbox.Pause(person, cancellationToken, only),
            ButtonsOnPower.LetGoFirst,
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Resume(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(
            spaceId,
            userId,
            botId,
            "sandbox.resumed",
            (sandbox, person, _) => sandbox.Resume(person, cancellationToken),
            ButtonsOnPower.LetGoAfter,
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Rebuild(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(
            spaceId,
            userId,
            botId,
            "sandbox.rebuilt",
            async (sandbox, person, only) =>
            {
                await sandbox.Rebuild(person, cancellationToken, only);
                return true;
            },
            ButtonsOnPower.Reset,
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> TurnOff(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(
            spaceId,
            userId,
            botId,
            "sandbox.turned_off",
            (sandbox, person, only) => sandbox.TurnOff(person, cancellationToken, only),
            ButtonsOnPower.Reset,
            cancellationToken);

    public Task<SandboxControlResult<SandboxScreenshot>> Screen(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Screen(spaceId, userId, botId, wake: true, cancellationToken);

    /// <summary>
    /// The sandbox's screen as a PNG. With wake off, taking a picture leaves the sandbox's lifecycle alone.
    /// Requests for the same sandbox and wake setting share one screenshot. Looking does not write an audit entry.
    /// </summary>
    public Task<SandboxControlResult<SandboxScreenshot>> Screen(Guid spaceId, Guid userId, Guid botId, bool wake, CancellationToken cancellationToken) =>
        Control(
            spaceId,
            userId,
            botId,
            null,
            (sandbox, person) => SharedScreenshot(sandbox, person, (spaceId, botId, wake), cancellationToken),
            cancellationToken);

    /// <summary>
    /// One click, typed text, key press, scroll, or pointer action, built by the same <see cref="DesktopCommands"/> a
    /// bot's tools use, with the same bounds and key list. A paused sandbox is resumed; one not made yet is not made
    /// for this. A button a pointer action holds down is always let go of: see <see cref="SandboxPointerHolds"/>. The
    /// <paramref name="sessionId"/> is the session the person is signed in with, which alone keeps a button it pressed
    /// held by moving.
    /// </summary>
    public async Task<SandboxControlResult<bool>> Input(
        Guid spaceId,
        Guid userId,
        Guid botId,
        SandboxInputRequest input,
        CancellationToken cancellationToken,
        Guid? sessionId = null)
    {
        var (built, problem) = BuildInput(input);

        if (built is null)
        {
            return new(false, SandboxControlRefusal.Invalid, problem);
        }

        var (action, doing, pointer) = built;
        var owner = new PointerOwner(userId, sessionId ?? Guid.Empty);

        return await Control(
            spaceId,
            userId,
            botId,
            new Audit("sandbox.input", new JsonObject { ["input"] = input.Kind }),
            async (sandbox, person) => await Act(
                sandbox,
                person,
                action,
                doing,
                Through(spaceId, botId, owner, pointer, cancellationToken),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a person's command line in the bot's sandbox, made on first use, for at most five minutes and never past
    /// the deployment's command limit. The command is not written to the audit log; that a command ran is.
    /// </summary>
    public async Task<SandboxControlResult<SandboxCommandResponse>> Run(
        Guid spaceId,
        Guid userId,
        Guid botId,
        string command,
        CancellationToken cancellationToken)
    {
        if (CommandProblem(command) is { } problem)
        {
            return new(null, SandboxControlRefusal.Invalid, problem);
        }

        return await Control(
            spaceId,
            userId,
            botId,
            new Audit("sandbox.command", null),
            async (sandbox, person) =>
            {
                var result = await sandbox.Run(person, command, commandLimit, cancellationToken);
                return new SandboxCommandResponse(result.ExitCode, result.Stdout, result.Stderr, result.Truncated, result.TimedOut, result.NewSandbox);
            },
            cancellationToken);
    }

    /// <summary>Lets go of every button a person still holds down, as this server stops.</summary>
    public ValueTask DisposeAsync() => heldButtons?.DisposeAsync() ?? ValueTask.CompletedTask;

    #endregion

    #region Acting

    /// <summary>A power control, then the state it left, read fresh. The audit row is written only when something changed.</summary>
    private async Task<SandboxControlResult<BotSandboxStatus>> Power(
        Guid spaceId,
        Guid userId,
        Guid botId,
        string action,
        Func<SandboxBroker, SandboxCaller, Guid?, Task<bool>> work,
        ButtonsOnPower buttons,
        CancellationToken cancellationToken)
    {
        var done = await Control(
            spaceId,
            userId,
            botId,
            new Audit(action, null),
            (sandbox, person) => WithHeldButtons(
                spaceId,
                sandbox,
                person,
                buttons,
                only => work(sandbox, person, only),
                cancellationToken),
            cancellationToken,
            auditWhen: changed => changed);

        return done.Refusal is SandboxControlRefusal.None
            ? await GetStatus(spaceId, userId, botId, cancellationToken)
            : new(null, done.Refusal, done.Problem);
    }

    /// <summary>
    /// The person's check in one short transaction, the work through the broker with none open, then one audit row in
    /// another for every attempt that reached the broker. The row says how the attempt ended. A refusal from the broker
    /// or the provider comes back as its own plain words; cancellation and failure still reach the caller.
    /// </summary>
    private async Task<SandboxControlResult<T>> Control<T>(
        Guid spaceId,
        Guid userId,
        Guid botId,
        Audit? audit,
        Func<SandboxBroker, SandboxCaller, Task<T>> work,
        CancellationToken cancellationToken,
        Func<T, bool>? auditWhen = null)
    {
        var tenant = scopes.For(spaceId, userId);

        if (await tenant.Transact(services => Check(services, spaceId, userId, botId, cancellationToken), cancellationToken) is { } refusal)
        {
            return Refuse<T>(refusal);
        }

        if (broker is null)
        {
            return new(default, SandboxControlRefusal.Refused, NoProvider);
        }

        T value;

        try
        {
            value = await work(broker, new SandboxCaller(tenant, botId));
        }
        catch (BuiltinToolException refused)
        {
            logger.LogInformation("A person's action on the sandbox of bot {BotId} was refused", botId);
            await Record<T>(tenant, userId, botId, audit, auditWhen, value: default!, "refused", rethrowing: false);
            return new(default, SandboxControlRefusal.Refused, refused.Message);
        }
        catch (OperationCanceledException)
        {
            await Record<T>(tenant, userId, botId, audit, auditWhen, value: default!, "cancelled", rethrowing: true);
            throw;
        }
        catch (Exception)
        {
            await Record<T>(tenant, userId, botId, audit, auditWhen, value: default!, "failed", rethrowing: true);
            throw;
        }

        WorkCompleted?.Invoke();
        await Record(tenant, userId, botId, audit, auditWhen, value, "done", rethrowing: false);
        return new(value);
    }

    /// <summary>
    /// One row for the attempt, in its own transaction with no request token: a client that disconnects cannot cancel
    /// the record of what its command did. Only the done outcome honours <paramref name="auditWhen"/>. A failure to
    /// write the row while another exception is already travelling is logged, not thrown in its place.
    /// </summary>
    private async Task Record<T>(
        RunTenant tenant,
        Guid userId,
        Guid botId,
        Audit? audit,
        Func<T, bool>? auditWhen,
        T value,
        string outcome,
        bool rethrowing)
    {
        if (audit is null || (outcome is "done" && !(auditWhen?.Invoke(value) ?? true)))
        {
            return;
        }

        var about = audit.About?.DeepClone().AsObject() ?? [];
        about["outcome"] = outcome;

        try
        {
            await tenant.Transact(
                services =>
                {
                    services.GetRequiredService<AuditLog>().Record(userId, audit.Action, AuditTargets.Bot, botId, null, null, about);
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }
        catch (Exception exception) when (rethrowing)
        {
            logger.LogError("Could not audit a sandbox action on bot {BotId}; the failure was {Failure}", botId, exception.GetType().Name);
        }
    }

    private async Task<SandboxScreenshot> SharedScreenshot(
        SandboxBroker sandbox,
        SandboxCaller person,
        (Guid SpaceId, Guid BotId, bool Wake) key,
        CancellationToken cancellationToken)
    {
        var shot = screens.GetOrAdd(key, _ => new Lazy<Task<SandboxScreenshot>>(() => TakeScreenshot(sandbox, person, key)));
        ScreenRequested?.Invoke();
        return await shot.Value.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// One screenshot, not cancelled by the request that started it, since others may be waiting on it. The provider
    /// calls under it have their own time limits.
    /// </summary>
    private async Task<SandboxScreenshot> TakeScreenshot(SandboxBroker sandbox, SandboxCaller person, (Guid SpaceId, Guid BotId, bool Wake) key)
    {
        try
        {
            return await sandbox.Screenshot(person, key.Wake, CancellationToken.None);
        }
        finally
        {
            screens.TryRemove(key, out _);
        }
    }

    #endregion

    #region Pointer

    private async Task<bool> Act(
        SandboxBroker sandbox,
        SandboxCaller person,
        CommandRequest action,
        string doing,
        Func<Guid, Func<Task>, Task> around,
        CancellationToken cancellationToken) =>
        await sandbox.Act(person, action, doing, create: false, cancellationToken, around) is not null
            ? true
            : throw new BuiltinToolException(NoSandboxYet);

    /// <summary>
    /// How an input runs on the sandbox it reaches, which is known only once the sandbox is acquired. A press is noted
    /// before its command runs, so that a command that fails, is cancelled, or runs out of time halfway still has its
    /// button let go of; a drag lets go by itself and is noted the same way for when it does not; a move keeps what
    /// this session holds; a release lets go of what anyone holds; any other input is refused only while a button
    /// could not be let go of.
    /// </summary>
    private Func<Guid, Func<Task>, Task> Through(
        Guid spaceId,
        Guid botId,
        PointerOwner owner,
        PointerAction? pointer,
        CancellationToken cancellationToken) =>
        (sandboxId, run) => pointer switch
        {
            null => Holds.Guard(spaceId, sandboxId, run),
            { Step: PointerStep.Down } => Holds.Press(
                new PointerPress(spaceId, sandboxId, botId, owner, pointer.Button),
                staysDown: true,
                run,
                cancellationToken),
            { Step: PointerStep.Drag } => Holds.Press(
                new PointerPress(spaceId, sandboxId, botId, owner, pointer.Button),
                staysDown: false,
                run,
                cancellationToken),
            { Step: PointerStep.Move } => Holds.Move(spaceId, sandboxId, owner, run),
            _ => Holds.Lift(spaceId, sandboxId, pointer.Button, run, cancellationToken),
        };

    /// <summary>
    /// Runs a power control with what is held down on the sandbox's display dealt with. Pausing keeps the display as it
    /// is, so a button still down would be down when it resumes: the pause is refused when one cannot be let go of.
    /// Resuming lets go of what a pause kept. Rebuilding and turning off destroy the display, which is what resets it,
    /// so what was held on it is forgotten once that worked.
    /// </summary>
    private async Task<bool> WithHeldButtons(
        Guid spaceId,
        SandboxBroker sandbox,
        SandboxCaller person,
        ButtonsOnPower buttons,
        Func<Guid?, Task<bool>> work,
        CancellationToken cancellationToken)
    {
        if (await sandbox.CurrentSandbox(person, cancellationToken) is not { } sandboxId)
        {
            return await work(null);
        }

        if (buttons is ButtonsOnPower.LetGoAfter)
        {
            var resumed = await work(sandboxId);
            await Holds.ReleaseAll(spaceId, sandboxId, cancellationToken);
            return resumed;
        }

        var done = false;
        var ran = await Holds.RunWhileReleased(
            spaceId,
            sandboxId,
            required: buttons is ButtonsOnPower.LetGoFirst,
            async () => done = await work(sandboxId),
            cancellationToken);

        if (!ran)
        {
            throw new BuiltinToolException(NotPausedWhileHeld);
        }

        if (done && buttons is ButtonsOnPower.Reset)
        {
            Holds.Forget(spaceId, sandboxId);
        }

        return done;
    }

    /// <summary>Lets go of a button a person held, as the person who pressed it, on the sandbox it was pressed
    /// on.</summary>
    private Task<ReleaseOutcome> ReleaseHeld(PointerPress held, CancellationToken cancellationToken)
    {
        if (broker is null)
        {
            return Task.FromResult(ReleaseOutcome.Gone);
        }

        var person = new SandboxCaller(scopes.For(held.SpaceId, held.Owner.UserId), held.BotId);
        return broker.Release(person, held.SandboxId, DesktopCommands.Release([held.Button]), cancellationToken);
    }

    #endregion

    #region Checks

    /// <summary>Null when the person may change the bot; otherwise why not. Not a member and no such bot look the same.</summary>
    private static async Task<SandboxControlRefusal?> Check(
        IServiceProvider services,
        Guid spaceId,
        Guid userId,
        Guid botId,
        CancellationToken cancellationToken)
    {
        var role = await services.GetRequiredService<AccountRepository>().FindRole(spaceId, userId, cancellationToken);

        if (role is null)
        {
            return SandboxControlRefusal.NotFound;
        }

        if (role is not (MembershipRole.Owner or MembershipRole.Admin))
        {
            return SandboxControlRefusal.NotPermitted;
        }

        return await services.GetRequiredService<BotRepository>().FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed }
            ? null
            : SandboxControlRefusal.NotFound;
    }

    private static SandboxControlResult<T> Refuse<T>(SandboxControlRefusal refusal) => new(default, refusal, refusal switch
    {
        SandboxControlRefusal.NotPermitted => "Owners and admins of this space use its bots' sandboxes.",
        _ => "This bot is not here.",
    });

    private static string? CommandProblem(string command) =>
        string.IsNullOrWhiteSpace(command) ? "Type a command to run."
        : command.Length > MaxCommandCharacters ? $"A command is at most {MaxCommandCharacters} characters."
        : command.Any(character => char.IsControl(character) && character is not '\n' and not '\t')
            ? "A command cannot hold control characters other than new lines and tabs."
            : null;

    /// <summary>
    /// The input as the command to run, built and checked as a bot's would be, or the plain sentence that refuses it.
    /// </summary>
    private static (BuiltInput? Built, string? Problem) BuildInput(SandboxInputRequest input)
    {
        try
        {
            var pointer = input.ToPointerAction();
            var (action, doing) = pointer is null ? BuildAction(input) : (pointer.Command, pointer.Doing);
            return (new BuiltInput(action, doing, pointer), null);
        }
        catch (BuiltinToolException wrong)
        {
            return (null, wrong.Message);
        }
    }

    /// <summary>The action as a program and its arguments, checked as a bot's would be. A wrong one is refused here.</summary>
    private static (CommandRequest Action, string Doing) BuildAction(SandboxInputRequest input) => input.Kind switch
    {
        "click" => (
            DesktopCommands.Click(
                input.X ?? throw new BuiltinToolException("A click needs x and y."),
                input.Y ?? throw new BuiltinToolException("A click needs x and y."),
                Word(input.Button, MouseButton.Left),
                input.Twice),
            "click there"),
        "type" => (DesktopCommands.Type(input.Text ?? string.Empty), "type the text"),
        "password" => (DesktopCommands.TypePassword(input.Text ?? string.Empty), "type the password"),
        "open_terminal" => (DesktopCommands.OpenTerminal(), "open the desktop terminal"),
        "key" => (DesktopCommands.Key(input.Keys ?? []), "press the keys"),
        "scroll" => (
            DesktopCommands.Scroll(
                Word<ScrollDirection>(input.Direction, null) ?? throw new BuiltinToolException("Scroll up or down."),
                input.Amount ?? 3,
                input.X,
                input.Y),
            "scroll"),
        _ => throw new BuiltinToolException("Choose a supported desktop action."),
    };

    private static T Word<T>(string? value, T fallback)
        where T : struct, Enum =>
        Word<T>(value, (T?)fallback) ?? fallback;

    private static T? Word<T>(string? value, T? fallback)
        where T : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        return Enum.GetNames<T>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Enum.Parse<T>(found)
            : throw new BuiltinToolException($"{value} is not one of: {string.Join(", ", Enum.GetNames<T>().Select(name => name.ToLowerInvariant()))}.");
    }

    #endregion

    #region Reading The State

    private async Task<BotSandboxStatus> ReadStatus(IServiceProvider services, Guid userId, Guid botId, CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();
        var bot = await bots.FindBot(botId, cancellationToken) ?? throw new InvalidOperationException("The bot was checked in this transaction.");
        var available = broker is not null;
        var offered = deployment.Value.Rules.AllowDesktopSandboxes;

        if (bot.SandboxPolicy is SandboxPolicy.None)
        {
            return new BotSandboxStatus(available, HasSandbox: false, "none", null, Desktop: false, BotWorking: false, offered);
        }

        var ownerId = SandboxGroups.OwnerOf(bot.Id, await bots.ListSandboxPath(bot.Id, cancellationToken));
        var owner = ownerId == bot.Id ? bot : await bots.FindBot(ownerId, cancellationToken) ?? bot;
        var sandboxes = services.GetRequiredService<SandboxRepository>();
        var live = await sandboxes.FindLiveForOwner(ownerId, cancellationToken);
        var replacing = await sandboxes.HasRetiringForOwner(ownerId, cancellationToken);

        // A row given up before the provider made it holds no provider id: a start that failed, cleared by the sweep.
        var startFailed = live is null
            && !replacing
            && await sandboxes.FindNewestForOwner(ownerId, cancellationToken) is { ProviderSandboxId.Length: 0, State: SandboxState.Destroyed };
        var working = await services.GetRequiredService<RunRepository>().IsBotWorking(bot.Id, cancellationToken);

        return new BotSandboxStatus(
            available,
            HasSandbox: true,
            StateOf(live, replacing, time.GetUtcNow()),
            live?.LastUsedAt,
            owner.SandboxSettings.Desktop && offered,
            working,
            offered,
            startFailed,
            await FindHold(services, ownerId, userId, cancellationToken));
    }

    /// <summary>The run, of any bot that shares this sandbox, that holds its screen for a person, if one does.</summary>
    private static async Task<SandboxHold?> FindHold(IServiceProvider services, Guid ownerId, Guid userId, CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();

        foreach (var run in await services.GetRequiredService<RunRepository>().ListRunsWaitingFor(RunReasons.AwaitingTakeover, cancellationToken))
        {
            if (SandboxGroups.OwnerOf(run.BotId, await bots.ListSandboxPath(run.BotId, cancellationToken)) == ownerId)
            {
                return new SandboxHold(run.RootRunId, run.PrincipalUserId == userId, run.WaitingSince);
            }
        }

        return null;
    }

    /// <summary>
    /// The state in a word. The provider pauses a sandbox on its own once its idle time runs out after the last use, so
    /// a running row past that time is paused, without asking the provider.
    /// </summary>
    private static string StateOf(Sandbox? live, bool replacing, DateTimeOffset now) => live switch
    {
        null when replacing => "replacing",
        null => "none",
        { State: SandboxState.Creating } => "starting",
        { State: SandboxState.Paused } => "paused",
        { ExpiresAt: { } expires } when expires <= now => "paused",
        _ => "running",
    };

    #endregion

    #region Nested Types

    /// <summary>An input ready to run: its command, what it does, and the pointer action when it is one.</summary>
    private sealed record BuiltInput(CommandRequest Action, string Doing, PointerAction? Pointer);

    /// <summary>What a power control does about the mouse buttons held down on the sandbox's display.</summary>
    private enum ButtonsOnPower
    {
        /// <summary>Let go of them before the control, and refuse it when that fails: pausing keeps the
        /// display.</summary>
        LetGoFirst,

        /// <summary>Let go of what a pause kept, after the control.</summary>
        LetGoAfter,

        /// <summary>The control destroys the display: let go as far as possible, and forget what was held once it
        /// worked.</summary>
        Reset,
    }

    /// <summary>What the audit log records for an action: its name and, at most, which kind of input it was.</summary>
    private sealed record Audit(string Action, JsonObject? About);

    #endregion
}
