namespace Drover.Engine.Sandboxes;

/// <summary>
/// What an input read before it looked for its sandbox: the power counter, and when, on the monotonic clock. Both are
/// read in one step, so a counter is never paired with a later time. A sample older than the power controls are
/// remembered is refused, so an input is never admitted on a record that has been forgotten.
/// </summary>
internal readonly record struct PowerSample(int Epoch, long Timestamp);


/// <summary>The session of a person that pressed a button, which alone may keep it held.</summary>
internal sealed record PointerOwner(Guid UserId, Guid SessionId);


/// <summary>
/// One press of a mouse button on one physical sandbox, and who made it through which bot.
/// <see cref="ProviderSandboxId"/> is the provider's id of that sandbox when it was pressed, which every later release
/// checks, so a release never reaches another sandbox.
/// </summary>
internal sealed record PointerPress(
    Guid SpaceId,
    Guid SandboxId,
    Guid BotId,
    PointerOwner Owner,
    MouseButton Button,
    string ProviderSandboxId = "");


/// <summary>
/// A person's input on one physical sandbox, in the steps it runs in under that sandbox's gate. <see cref="Acquire"/>
/// gets the sandbox ready (resumes it and starts its display) and answers the provider's id for it. <see cref="Run"/>
/// is the input's own work. <see cref="LetGo"/>, once acquired, lets go of buttons on the exact sandbox that was
/// acquired, whatever its row says; without it, the release every hold uses is used. <see cref="Refusal"/> asks
/// whether the person <see cref="Person"/> still may, before the sandbox is acquired and again right before
/// <see cref="Run"/> is sent: it answers the sentence that refuses them, or null.
/// </summary>
internal sealed record ScreenUse(
    Guid SandboxId,
    Func<Task<string>> Acquire,
    Func<Task> Run,
    Func<IReadOnlyList<MouseButton>, CancellationToken, Task<ReleaseOutcome>>? LetGo = null,
    Func<Task<string?>>? Refusal = null,
    Guid? Person = null);


/// <summary>
/// The mouse buttons people hold down on sandbox screens, so that every one is let go of. A held button is a state of
/// a physical display that no connection owns: a person whose connection is lost, or whose request fails or is
/// cancelled, would leave it down for the next person or bot to find.
/// <para>
/// A hold belongs to the physical sandbox, keyed by its space and its row, never by the bot that was asked: bots that
/// share a sandbox share its display, so they share its holds. It belongs to the session that pressed the button,
/// which alone keeps it held by moving. Each display has one gate. Every person's input on it, every release of a
/// button on it, and every power control's change to it run under that gate, so none of them comes between another's
/// steps.
/// </para>
/// <para>
/// A button is let go of when the request that pressed it fails or is cancelled, before its audit row is written;
/// <see cref="Limit"/> after the press or the last move of its session; <see cref="MaxHold"/> after it first went
/// down, whatever is done; before the sandbox is paused, rebuilt or turned off from the screen, and as part of a
/// resume; before a bot's call uses the sandbox; and when the server stops. A release that fails is tried again after
/// the same time, and after <see cref="MaxFailedReleases"/> failures in a row the screen takes no input until the
/// button is let go of or the sandbox is known to be gone, which is checked every <see cref="StuckProbe"/>. Nothing
/// here keeps a position: only which buttons are down, on which sandbox, and by whom.
/// </para>
/// </summary>
internal sealed class SandboxPointerHolds(
    TimeProvider time,
    ILogger logger,
    Func<PointerPress, CancellationToken, Task<ReleaseOutcome>> release) : IAsyncDisposable
{
    #region Private Fields

    private const string StuckWhy = "A mouse button on this screen could not be let go of, so it takes no input until";

    private const string StuckWhat = "it is. Let go of the button, or rebuild the sandbox.";

    private const string StuckSentence = $"{StuckWhy} {StuckWhat}";

    private const string StoppingSentence = "This server is stopping, so nothing was pressed. Try again soon.";

    private const string PoweredSentence =
        "The sandbox was paused or replaced while this waited, so nothing was sent to it. Try again.";

    private const string TooOldSentence =
        "This waited too long to be sure the sandbox was unchanged, so nothing was sent to it. Try again.";

    private const string DueSentence =
        "A mouse button on this screen was due to be let go of and could not be, so nothing was sent. Try again.";

    private const string TakenSentence = "Another person is holding that mouse button on this screen.";

    private const string FullWhat =
        "Too many mouse buttons are held on sandbox screens right now, so nothing was pressed.";

    private const string FullSentence = $"{FullWhat} Let go of one, or try again in a moment.";

    private static readonly TimeSpan limit = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan maxHold = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan stuckProbe = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan stopRetry = TimeSpan.FromSeconds(1);

    /// <summary>How long a power control is remembered: longer than any input can wait to be admitted.</summary>
    private static readonly TimeSpan powerMemory = TimeSpan.FromMinutes(15);

    private readonly Lock sync = new();

    private readonly Dictionary<(Guid SpaceId, Guid SandboxId), Screen> screens = [];

    private readonly List<Task> expiring = [];

    /// <summary>The power controls that began, by the space and the physical sandbox they acted on, and when.</summary>
    private readonly Dictionary<(Guid SpaceId, Guid SandboxId), (int Epoch, long Timestamp)> powered = [];

    private bool closed;

    private int powerEpoch;

    #endregion

    #region Properties

    /// <summary>
    /// Runs when work on a screen finds its gate taken and is about to wait for it. A test uses it to know the work has
    /// reached the gate. Not used outside tests.
    /// </summary>
    internal Action? Waiting { get; set; }

    /// <summary>
    /// Runs when a stop has started everything it waits for and is about to wait. A test uses it to know the stop is
    /// waiting. Not used outside tests.
    /// </summary>
    internal Action? Stopping { get; set; }

    /// <summary>
    /// A number that grows each time a power control (pause, rebuild, turn off) begins to act on a sandbox, read with
    /// the monotonic time in one step. An input reads it before it looks for its sandbox and gives it back, and is
    /// refused at the gate only if a control began on that input's own sandbox in its own space since. A control on
    /// another sandbox or in another space changes the number and nothing else.
    /// </summary>
    public PowerSample PowerEpoch
    {
        get
        {
            lock (sync)
            {
                return new PowerSample(powerEpoch, time.GetTimestamp());
            }
        }
    }

    /// <summary>How long a held button may go without a move from the session that pressed it.</summary>
    public static TimeSpan Limit => limit;

    /// <summary>The longest a button is held, however often its session moves or presses it again.</summary>
    public static TimeSpan MaxHold => maxHold;

    /// <summary>How often a button still held is tried again while the server stops.</summary>
    public static TimeSpan StopRetry => stopRetry;

    /// <summary>How often a button that could not be let go of is tried again once the screen takes no input.</summary>
    public static TimeSpan StuckProbe => stuckProbe;

    /// <summary>Failed releases in a row after which the screen takes no input.</summary>
    public static int MaxFailedReleases => 6;

    /// <summary>The most screens with a button held down, across the server.</summary>
    public static int MaxHeldScreens => 64;

    /// <summary>The most screens with a button held down in one space.</summary>
    public static int MaxHeldScreensPerSpace => 8;

    #endregion

    #region Public Methods

    /// <summary>A press whose work is one step, as a test makes it.</summary>
    public Task Press(
        PointerPress press,
        bool staysDown,
        Func<Task> run,
        CancellationToken cancellationToken,
        PowerSample? epoch = null) =>
        Press(press, staysDown, Plain(press.SandboxId, press.ProviderSandboxId, run), cancellationToken, epoch);

    /// <summary>
    /// Runs a press under the screen's gate: it is refused, before the sandbox is acquired, when the server is
    /// stopping, a power control began on this sandbox since <paramref name="epoch"/> was read, the screen takes no
    /// input, another session holds the button, or too many screens have a button held. The press is noted once the
    /// sandbox is acquired and before its command runs, so a command that fails or is cancelled halfway, or whose
    /// audit row cannot be written, has its button let go of before the exception goes on. A second press of a button
    /// its session holds is the same press: its time limit keeps running while it runs. With
    /// <paramref name="staysDown"/> off (a drag, which lets go by itself) the button is forgotten once it worked.
    /// </summary>
    public async Task Press(
        PointerPress press,
        bool staysDown,
        ScreenUse use,
        CancellationToken cancellationToken,
        PowerSample? epoch = null)
    {
        RefuseWhenStopping();
        await OnScreen(
            press.SpaceId,
            press.SandboxId,
            async screen =>
            {
                await ReleaseDue(screen, refuse: false);
                Admit(screen, press, epoch);
                RefuseDue(screen);
                await RefuseUnauthorized(screen, use);
                var acquired = press with { ProviderSandboxId = await use.Acquire() };
                await ReleaseDue(screen, refuse: true);
                await RefuseUnauthorized(screen, use);
                var held = Register(screen, acquired);
                await RunPressed(screen, held, use);
                Settle(screen, held, staysDown);
                return true;
            },
            cancellationToken);
    }

    /// <summary>A move whose work is one step, as a test makes it.</summary>
    public Task Move(Guid spaceId, Guid sandboxId, PointerOwner owner, Func<Task> run) =>
        Move(spaceId, sandboxId, owner, Plain(sandboxId, string.Empty, run), null, CancellationToken.None);

    /// <summary>
    /// Runs a move under the screen's gate. A button held by this session stays held for another
    /// <see cref="Limit"/>, as far as <see cref="MaxHold"/> allows; another session's moves keep nothing.
    /// </summary>
    public Task Move(
        Guid spaceId,
        Guid sandboxId,
        PointerOwner owner,
        ScreenUse use,
        PowerSample? epoch,
        CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                await ReleaseDue(screen, refuse: false);
                RefuseUnsafe(screen, spaceId, sandboxId, epoch, refuseStuck: true);
                RefuseDue(screen);
                await RefuseUnauthorized(screen, use);
                await use.Acquire();
                await ReleaseDue(screen, refuse: true);
                await RefuseUnauthorized(screen, use);
                var at = time.GetTimestamp();
                await use.Run();
                Renew(screen, owner, at);
                return true;
            },
            cancellationToken);

    /// <summary>An input whose work is one step, as a test makes it.</summary>
    public Task Guard(Guid spaceId, Guid sandboxId, Func<Task> run) =>
        Guard(spaceId, sandboxId, Plain(sandboxId, string.Empty, run), null, CancellationToken.None);

    /// <summary>
    /// Runs an input that needs a display with no stuck button under the screen's gate: it is refused when a button
    /// could not be let go of, or a power control began on this sandbox since <paramref name="epoch"/> was read.
    /// </summary>
    public Task Guard(
        Guid spaceId,
        Guid sandboxId,
        ScreenUse use,
        PowerSample? epoch,
        CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                await ReleaseDue(screen, refuse: false);
                RefuseUnsafe(screen, spaceId, sandboxId, epoch, refuseStuck: true);
                RefuseDue(screen);
                await RefuseUnauthorized(screen, use);
                await use.Acquire();
                await ReleaseDue(screen, refuse: true);
                await RefuseUnauthorized(screen, use);
                await use.Run();
                return true;
            },
            cancellationToken);

    /// <summary>A release whose work is one step, as a test makes it.</summary>
    public Task Lift(
        Guid spaceId,
        Guid sandboxId,
        MouseButton button,
        Func<Task> run,
        CancellationToken cancellationToken) =>
        Lift(spaceId, sandboxId, button, Plain(sandboxId, string.Empty, run), null, cancellationToken);

    /// <summary>
    /// Runs the release of a button under the screen's gate and forgets it once it worked, whoever pressed it: a stuck
    /// screen never refuses it. This is how a button the server could not let go of is let go of by a person. It is
    /// refused only when a power control began on this sandbox since <paramref name="epoch"/> was read: that control
    /// let go of everything first, or reset the display.
    /// </summary>
    public Task Lift(
        Guid spaceId,
        Guid sandboxId,
        MouseButton button,
        ScreenUse use,
        PowerSample? epoch,
        CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                await ReleaseDue(screen, refuse: false);
                RefuseUnsafe(screen, spaceId, sandboxId, epoch, refuseStuck: false);
                await RefuseUnauthorized(screen, use);
                await use.Acquire();
                await ReleaseDue(screen, refuse: false);
                await RefuseUnauthorized(screen, use);
                await use.Run();

                lock (sync)
                {
                    Remove(screen, button);
                }

                return true;
            },
            cancellationToken);

    /// <summary>
    /// Lets go of every button held on this sandbox under its gate, and waits for it. True when none is held any more.
    /// A release that fails leaves the button held, and its time starts again.
    /// </summary>
    public Task<bool> ReleaseAll(Guid spaceId, Guid sandboxId, CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                await ReleaseEach(screen, cancellationToken);
                return !IsHeld(screen);
            },
            cancellationToken);

    /// <summary>
    /// Lets go of every button <paramref name="userId"/> holds on any screen of <paramref name="spaceId"/>, each under
    /// its screen's gate: the person may no longer use the space's sandboxes. Each release has its own deadline; one
    /// that cannot be confirmed is logged and stays with its record, whose deadlines still apply.
    /// </summary>
    public async Task ReleaseHeldBy(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var sandboxId in ScreensHeldBy(spaceId, userId))
        {
            await OnScreen(
                spaceId,
                sandboxId,
                async screen =>
                {
                    await ReleaseOwned(screen, userId);
                    return true;
                },
                cancellationToken);
        }
    }

    /// <summary>Whether a button is held on this sandbox now. Cheap, so a bot's call asks before every use.</summary>
    public bool IsHeldOn(Guid spaceId, Guid sandboxId)
    {
        lock (sync)
        {
            return screens.TryGetValue((spaceId, sandboxId), out var screen) && screen.Presses.Count > 0;
        }
    }

    /// <summary>
    /// Completes when every release the time limit has started has ended, whether it let go or failed and set its
    /// limit again. A test that moves the clock uses it, so that the clock never moves under a release that is running.
    /// Not used outside tests.
    /// </summary>
    internal Task Quiet() => Task.WhenAll(Running());

    /// <summary>The sandbox was destroyed, which let go of everything on its display: its holds go.</summary>
    public void Forget(Guid spaceId, Guid sandboxId)
    {
        lock (sync)
        {
            if (!screens.TryGetValue((spaceId, sandboxId), out var screen))
            {
                return;
            }

            foreach (var held in screen.Presses.Values)
            {
                held.Timer?.Dispose();
            }

            screen.Presses.Clear();
            Discard(screen);
        }
    }

    /// <summary>
    /// Stops taking presses and lets go of every button still held, on every screen at once, each screen's buttons all
    /// tried together, and waits for that and for the releases the time limit started. The whole stop is bounded by
    /// <see cref="Limit"/> on the clock, whether or not the provider honours cancellation. A screen whose gate is still
    /// held then lets go of what is held on it when that work ends, since the server has stopped.
    /// </summary>
    public ValueTask DisposeAsync() => new(Stop(CancellationToken.None));

    /// <summary>
    /// As <see cref="DisposeAsync"/>, from the host's stop, while the services a release needs still work. The wait
    /// also ends when <paramref name="host"/> does. Each button still held when the wait ends is logged by sandbox.
    /// </summary>
    public async Task Stop(CancellationToken host)
    {
        if (Close() is not { } stop)
        {
            return;
        }

        using var outer = new CancellationTokenSource(limit, time);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(host, outer.Token);
        List<Task> waiting = [.. stop.Screens.Select(screen => ReleaseOnStop(screen, bound.Token)), .. stop.Running];
        Stopping?.Invoke();

        try
        {
            await Task.WhenAll(waiting).WaitAsync(limit, time, host);
        }
        catch (Exception ended) when (ended is TimeoutException or OperationCanceledException)
        {
            logger.LogError(
                "Letting go of held mouse buttons was still running after {Seconds} seconds as the server stopped",
                limit.TotalSeconds);
        }

        foreach (var held in StillHeld())
        {
            logger.LogError(
                "A mouse button on sandbox {SandboxId} was still held when the server stopped",
                held.Request.SandboxId);
        }

        lock (sync)
        {
            foreach (var press in screens.Values.SelectMany(screen => screen.Presses.Values))
            {
                press.Timer?.Dispose();
            }
        }
    }

    /// <summary>A power control whose work is one step that begins at once, as a test makes it.</summary>
    public Task<bool> RunWhileReleased(
        Guid spaceId,
        Guid sandboxId,
        bool required,
        Func<Task> work,
        CancellationToken cancellationToken) =>
        RunWhileReleased(
            spaceId,
            sandboxId,
            required,
            begin =>
            {
                begin();
                return work();
            },
            cancellationToken);

    /// <summary>
    /// Lets go of every button held on this sandbox and, with the screen's gate still held so that no input or release
    /// can come between, runs <paramref name="work"/>. False, and the work is not run, when
    /// <paramref name="required"/> is set and a button could not be let go of. The work calls the action it is given
    /// once it has checked that the sandbox is still the one gated and is about to ask the provider to change it: from
    /// then on, and again when it ends however it ended, any input that read <see cref="PowerEpoch"/> before is
    /// refused. A control that ends before that point changed nothing and refuses nothing.
    /// </summary>
    public Task<bool> RunWhileReleased(
        Guid spaceId,
        Guid sandboxId,
        bool required,
        Func<Action, Task> work,
        CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                await ReleaseEach(screen, cancellationToken);

                if (required && IsHeld(screen))
                {
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var begun = false;

                try
                {
                    await work(() =>
                    {
                        if (!begun)
                        {
                            begun = true;
                            MarkPower(spaceId, sandboxId);
                        }
                    });

                    return true;
                }
                finally
                {
                    if (begun)
                    {
                        MarkPower(spaceId, sandboxId);
                    }
                }
            },
            cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> (a resume) under the screen's gate and, when it did something, lets go of what a
    /// pause kept held before the gate is given back, so no press can come between and none admitted after is let go
    /// of by it.
    /// </summary>
    public Task<bool> RunThenRelease(
        Guid spaceId,
        Guid sandboxId,
        Func<Task<bool>> work,
        CancellationToken cancellationToken) =>
        OnScreen(
            spaceId,
            sandboxId,
            async screen =>
            {
                var done = await work();

                if (done)
                {
                    await ReleaseEach(screen, cancellationToken);
                }

                return done;
            },
            cancellationToken);

    #endregion

    #region The Gate

    private async Task<T> OnScreen<T>(
        Guid spaceId,
        Guid sandboxId,
        Func<Screen, Task<T>> work,
        CancellationToken cancellationToken)
    {
        var screen = Enter(spaceId, sandboxId);

        try
        {
            if (screen.Gate.CurrentCount is 0)
            {
                Waiting?.Invoke();
            }

            await screen.Gate.WaitAsync(cancellationToken);

            try
            {
                return await work(screen);
            }
            finally
            {
                await LeaveGate(screen);
            }
        }
        finally
        {
            Leave(screen);
        }
    }

    /// <summary>
    /// Gives the gate back, first letting go of every press that came due while the work held it, so a deadline that
    /// passed during slow work (a provider command, recording the use, the audit row) is kept the moment that work
    /// ends. When the server stopped while this held the gate, the stop could not take it, so everything still held on
    /// the display is let go of here instead, tried again until <see cref="Limit"/> runs out.
    /// </summary>
    private async Task LeaveGate(Screen screen)
    {
        try
        {
            if (IsClosed())
            {
                if (IsHeld(screen))
                {
                    using var bound = new CancellationTokenSource(limit, time);
                    await ReleaseUntilDone(screen, bound.Token);
                }
            }
            else
            {
                await ReleaseDue(screen, refuse: false);
            }
        }
        finally
        {
            screen.Gate.Release();
        }
    }

    #endregion

    #region Presses

    private void RefuseWhenStopping()
    {
        lock (sync)
        {
            if (closed)
            {
                throw new BuiltinToolException(StoppingSentence);
            }
        }
    }

    /// <summary>Every check a press passes before its sandbox is acquired, so a refused press wakes nothing.</summary>
    private void Admit(Screen screen, PointerPress press, PowerSample? epoch)
    {
        lock (sync)
        {
            if (closed)
            {
                throw new BuiltinToolException(StoppingSentence);
            }

            RefusePowered(press.SpaceId, press.SandboxId, epoch);

            if (IsStuck(screen))
            {
                throw new BuiltinToolException(StuckSentence);
            }

            if (screen.Presses.TryGetValue(press.Button, out var existing))
            {
                if (existing.Request.Owner != press.Owner)
                {
                    throw new BuiltinToolException(TakenSentence);
                }

                return;
            }

            RefuseWhenFull(screen, press.SpaceId);
        }
    }

    /// <summary>
    /// The press, noted with the gate held and its timer armed, just before its command is sent: the one its session
    /// already holds, kept from now as a move would (never past its longest hold), or a new one whose deadlines start
    /// now. From here the record owns the cleanup of the button until a release is confirmed or the sandbox is
    /// confirmed gone. Refused when the server began to stop while the sandbox was acquired.
    /// </summary>
    private PressRecord Register(Screen screen, PointerPress press)
    {
        lock (sync)
        {
            if (closed)
            {
                throw new BuiltinToolException(StoppingSentence);
            }

            var now = time.GetTimestamp();

            if (screen.Presses.TryGetValue(press.Button, out var existing))
            {
                existing.IdleFrom = now;
                Arm(screen, existing, Remaining(existing));
                return existing;
            }

            RefuseWhenFull(screen, press.SpaceId);
            var held = new PressRecord(press, now);
            screen.Presses[press.Button] = held;
            Arm(screen, held, Remaining(held));
            return held;
        }
    }

    /// <summary>A new screen with a button held is admitted only within the server's and the space's bounds.</summary>
    private void RefuseWhenFull(Screen screen, Guid spaceId)
    {
        if (screen.Presses.Count > 0)
        {
            return;
        }

        var holding = screens.Values.Where(other => other.Presses.Count > 0).ToList();

        if (holding.Count >= MaxHeldScreens
            || holding.Count(other => other.Key.SpaceId == spaceId) >= MaxHeldScreensPerSpace)
        {
            throw new BuiltinToolException(FullSentence);
        }
    }

    private async Task RunPressed(Screen screen, PressRecord held, ScreenUse use)
    {
        try
        {
            await use.Run();
        }
        catch (Exception)
        {
            await ReleaseHeld(screen, held, CancellationToken.None, use.LetGo);
            throw;
        }
    }

    /// <summary>
    /// The press worked. A drag is already over, so it is forgotten. A held button keeps the deadlines it was given
    /// when it was registered: a press whose work outlasted them is let go of as the work leaves the gate.
    /// </summary>
    private void Settle(Screen screen, PressRecord held, bool staysDown)
    {
        lock (sync)
        {
            if (!staysDown && IsCurrent(screen, held))
            {
                Remove(screen, held.Request.Button);
            }
        }
    }

    /// <summary>
    /// Keeps this session's buttons from <paramref name="at"/>, the moment the move was sent, when none of them was
    /// due: a deadline that passes while the move runs still lets go of the button, at <paramref name="at"/> plus
    /// <see cref="Limit"/>. Called with the gate held.
    /// </summary>
    private void Renew(Screen screen, PointerOwner owner, long at)
    {
        lock (sync)
        {
            foreach (var held in screen.Presses.Values.Where(held => held.Request.Owner == owner && !held.Stuck))
            {
                held.IdleFrom = Math.Max(held.IdleFrom, at);
                Arm(screen, held, Remaining(held));
            }
        }
    }

    /// <summary>
    /// The last check before an input is sent, after every slower step: whether the person still may. When they may
    /// not (their role was taken away, or the bot is gone), every button they hold on this screen is let go of here,
    /// under the gate, before the input is refused, so losing the role ends their holds at once. A release that cannot
    /// be confirmed is logged and stays with its record, whose deadlines still apply.
    /// </summary>
    private async Task RefuseUnauthorized(Screen screen, ScreenUse use)
    {
        if (use.Refusal is not { } check || await check() is not { } refused)
        {
            return;
        }

        if (use.Person is { } person)
        {
            await ReleaseOwned(screen, person);
        }

        throw new BuiltinToolException(refused);
    }

    /// <summary>Lets go of every button <paramref name="userId"/> holds on this screen, called with its gate held.</summary>
    private async Task ReleaseOwned(Screen screen, Guid userId)
    {
        foreach (var held in Snapshot(screen).Where(held => held.Request.Owner.UserId == userId))
        {
            using var bound = new CancellationTokenSource(limit, time);

            try
            {
                if (!await ReleaseHeld(screen, held, bound.Token))
                {
                    logger.LogWarning(
                        "A mouse button on sandbox {SandboxId} held by a person who may no longer use it was not let go of",
                        held.Request.SandboxId);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Letting go of a mouse button on sandbox {SandboxId} for a person who lost their role ran out of time",
                    held.Request.SandboxId);
                Fail(screen, held);
            }
        }
    }

    private Guid[] ScreensHeldBy(Guid spaceId, Guid userId)
    {
        lock (sync)
        {
            return
            [
                .. screens.Values
                    .Where(screen => screen.Key.SpaceId == spaceId
                        && screen.Presses.Values.Any(held => held.Request.Owner.UserId == userId))
                    .Select(screen => screen.Key.SandboxId),
            ];
        }
    }

    /// <summary>The checks every input passes under the gate before its sandbox is acquired.</summary>
    private void RefuseUnsafe(Screen screen, Guid spaceId, Guid sandboxId, PowerSample? epoch, bool refuseStuck)
    {
        lock (sync)
        {
            RefusePowered(spaceId, sandboxId, epoch);

            if (refuseStuck && IsStuck(screen))
            {
                throw new BuiltinToolException(StuckSentence);
            }
        }
    }

    /// <summary>Called with the lock held.</summary>
    private void RefusePowered(Guid spaceId, Guid sandboxId, PowerSample? epoch)
    {
        if (epoch is not { } read)
        {
            return;
        }

        if (time.GetElapsedTime(read.Timestamp) > powerMemory)
        {
            // The marks an input that old could need may be forgotten, so it cannot be told nothing happened.
            throw new BuiltinToolException(TooOldSentence);
        }

        if (powered.TryGetValue((spaceId, sandboxId), out var power) && power.Epoch > read.Epoch)
        {
            throw new BuiltinToolException(PoweredSentence);
        }
    }

    /// <summary>
    /// Records that a power control began, or ended, on this sandbox, and forgets the ones older than any input can
    /// still be waiting on, by the monotonic clock. Only the sandbox's own space and id are marked.
    /// </summary>
    private void MarkPower(Guid spaceId, Guid sandboxId)
    {
        lock (sync)
        {
            powerEpoch++;
            powered[(spaceId, sandboxId)] = (powerEpoch, time.GetTimestamp());

            List<(Guid, Guid)> forgotten =
            [
                .. powered.Where(entry => time.GetElapsedTime(entry.Value.Timestamp) > powerMemory)
                    .Select(entry => entry.Key),
            ];

            foreach (var key in forgotten)
            {
                powered.Remove(key);
            }
        }
    }

    private static ScreenUse Plain(Guid sandboxId, string providerSandboxId, Func<Task> run) =>
        new(sandboxId, () => Task.FromResult(providerSandboxId), run);

    #endregion

    #region Letting Go

    /// <summary>
    /// Lets go of one press, called with the screen's gate held, through <paramref name="letGo"/> when given and the
    /// release every hold uses when not. The press is forgotten only when that worked, or the sandbox is known to be
    /// out of use; otherwise it stays held and is tried again, until it has failed too many times in a row and the
    /// screen takes no input.
    /// </summary>
    private async Task<bool> ReleaseHeld(
        Screen screen,
        PressRecord held,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<MouseButton>, CancellationToken, Task<ReleaseOutcome>>? letGo = null)
    {
        try
        {
            var outcome = letGo is null
                ? await release(held.Request, cancellationToken)
                : await letGo([held.Request.Button], cancellationToken);

            if (outcome is ReleaseOutcome.Released or ReleaseOutcome.Gone)
            {
                lock (sync)
                {
                    Drop(screen, held);
                }

                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller ended the work: that is not a failure of the release, which is tried again.
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(
                "Letting go of a mouse button on sandbox {SandboxId} failed; the failure was {Failure}",
                held.Request.SandboxId,
                failure.GetType().Name);
        }

        Fail(screen, held);
        return false;
    }

    /// <summary>
    /// Lets go of every press that is due by either deadline, called with the gate held, so no input runs on the
    /// display, or keeps or presses a button again, after its deadline while the timer's release waits for the gate.
    /// With <paramref name="refuse"/> set, the input is refused when a due button could not be let go of, rather than
    /// run with it still down. The answer is the moment after, which a move keeps its session's buttons from.
    /// </summary>
    private async Task<long> ReleaseDue(Screen screen, bool refuse)
    {
        foreach (var held in Snapshot(screen).Where(IsReadyToRelease))
        {
            using var bound = new CancellationTokenSource(limit, time);

            try
            {
                await ReleaseHeld(screen, held, bound.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Letting go of a due mouse button on sandbox {SandboxId} ran out of time",
                    held.Request.SandboxId);
                Fail(screen, held);
            }
        }

        if (refuse)
        {
            RefuseDue(screen);
        }

        return time.GetTimestamp();
    }

    /// <summary>Refuses an input while a due button could not be let go of: it would run with it down.</summary>
    private void RefuseDue(Screen screen)
    {
        if (Snapshot(screen).Any(IsDueToRelease))
        {
            throw new BuiltinToolException(DueSentence);
        }
    }

    private bool IsDueToRelease(PressRecord held)
    {
        lock (sync)
        {
            return !held.Stuck && IsDue(held);
        }
    }

    /// <summary>Due, and not within the wait after a release of it failed, which its own timer ends.</summary>
    private bool IsReadyToRelease(PressRecord held)
    {
        lock (sync)
        {
            return !held.Stuck
                && IsDue(held)
                && (held.FailedAt is not { } failed || time.GetElapsedTime(failed) >= limit);
        }
    }

    /// <summary>Lets go of each press in turn, called with the gate held. Cancellation ends it.</summary>
    private async Task ReleaseEach(Screen screen, CancellationToken cancellationToken)
    {
        foreach (var held in Snapshot(screen))
        {
            await ReleaseHeld(screen, held, cancellationToken);
        }
    }

    /// <summary>
    /// Lets go of every press at once, called with the gate held, so a slow release of one button does not keep the
    /// others from being tried. The bound running out is logged.
    /// </summary>
    private Task ReleaseEvery(Screen screen, CancellationToken cancellationToken) =>
        Task.WhenAll(Snapshot(screen).Select(held => ReleaseWithin(screen, held, cancellationToken)));

    private async Task ReleaseWithin(Screen screen, PressRecord held, CancellationToken cancellationToken)
    {
        try
        {
            await ReleaseHeld(screen, held, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "A mouse button on sandbox {SandboxId} was not let go of within {Seconds} seconds of the stop",
                held.Request.SandboxId,
                limit.TotalSeconds);
        }
    }

    private void Fail(Screen screen, PressRecord held)
    {
        lock (sync)
        {
            if (!IsCurrent(screen, held))
            {
                return;
            }

            held.Failures++;
            held.FailedAt = time.GetTimestamp();

            if (!held.Stuck && held.Failures >= MaxFailedReleases)
            {
                logger.LogError(
                    "A mouse button on sandbox {SandboxId} could not be let go of after {Attempts} tries",
                    held.Request.SandboxId,
                    held.Failures);
                held.Stuck = true;
            }

            Arm(screen, held, held.Stuck ? stuckProbe : limit);
        }
    }

    /// <summary>
    /// Lets go of what is held on one screen as the server stops, within the stop's budget. A screen whose gate is
    /// held past the budget is left to the work that holds it, which lets go when it ends.
    /// </summary>
    private async Task ReleaseOnStop(Screen screen, CancellationToken budget)
    {
        try
        {
            try
            {
                await screen.Gate.WaitAsync(budget);
            }
            catch (OperationCanceledException)
            {
                logger.LogError(
                    "A screen was busy {Seconds} seconds after the stop began; it lets go when that work ends",
                    limit.TotalSeconds);
                return;
            }

            try
            {
                await ReleaseUntilDone(screen, budget);
            }
            finally
            {
                screen.Gate.Release();
            }
        }
        finally
        {
            Leave(screen);
        }
    }

    /// <summary>
    /// Lets go of everything held on the screen as the server stops, every button at once, and tries what is still
    /// held again every <see cref="StopRetry"/> until nothing is or the budget runs out. Called with the gate held.
    /// </summary>
    private async Task ReleaseUntilDone(Screen screen, CancellationToken budget)
    {
        try
        {
            while (true)
            {
                await ReleaseEvery(screen, budget);

                if (!IsHeld(screen))
                {
                    return;
                }

                await Task.Delay(stopRetry, time, budget);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "A mouse button was still held on a screen {Seconds} seconds after the stop began",
                limit.TotalSeconds);
        }
    }

    #endregion

    #region The Time Limit

    /// <summary>Asks for a check of the press after <paramref name="due"/>. Called with the lock held.</summary>
    private void Arm(Screen screen, PressRecord held, TimeSpan due)
    {
        held.Timer?.Dispose();
        held.Timer = null;

        if (closed)
        {
            return;
        }

        held.Timer = time.CreateTimer(_ => Expire(screen, held), null, due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The time until the press is due: the nearer of its two limits, and never below none.</summary>
    private TimeSpan Remaining(PressRecord held)
    {
        var idle = limit - time.GetElapsedTime(held.IdleFrom);
        var longest = maxHold - time.GetElapsedTime(held.PressedAt);
        var nearer = idle < longest ? idle : longest;
        return nearer > TimeSpan.Zero ? nearer : TimeSpan.Zero;
    }

    /// <summary>Due when either limit passed, by the monotonic clock. A stuck press is always due.</summary>
    private bool IsDue(PressRecord held) =>
        held.Stuck
        || time.GetElapsedTime(held.IdleFrom) >= limit
        || time.GetElapsedTime(held.PressedAt) >= maxHold;

    /// <summary>
    /// A timer for the press ran out. The work is admitted and registered in one step, so a stop that closes after
    /// this sees it and waits for it.
    /// </summary>
    private void Expire(Screen screen, PressRecord held)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (sync)
        {
            if (closed || !IsCurrent(screen, held))
            {
                return;
            }

            screen.Users++;
            expiring.Add(done.Task);
        }

        _ = RunExpiry(screen, held, done);
    }

    private async Task RunExpiry(Screen screen, PressRecord held, TaskCompletionSource done)
    {
        try
        {
            logger.LogInformation(
                "A mouse button held on sandbox {SandboxId} is being let go of after {Seconds} seconds",
                held.Request.SandboxId,
                limit.TotalSeconds);
            await ReleaseExpired(screen, held);
        }
        catch (Exception failure)
        {
            logger.LogError(
                "Letting go of a mouse button on sandbox {SandboxId} ended with {Failure}",
                held.Request.SandboxId,
                failure.GetType().Name);
        }
        finally
        {
            lock (sync)
            {
                expiring.Remove(done.Task);
            }

            done.SetResult();
        }
    }

    /// <summary>
    /// Under the gate, the press is let go of only if it is still the one the timer was for and it is due by its
    /// deadlines; one kept since is given a timer for what is left. A due time is never renewed away.
    /// </summary>
    private async Task ReleaseExpired(Screen screen, PressRecord held)
    {
        using var bound = new CancellationTokenSource(limit, time);

        try
        {
            if (!await TakeGate(screen, held, bound.Token))
            {
                return;
            }

            try
            {
                if (IsDueNow(screen, held))
                {
                    await ReleaseHeld(screen, held, bound.Token);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Letting go of a mouse button on sandbox {SandboxId} ran out of time; it is tried again",
                    held.Request.SandboxId);
                Fail(screen, held);
            }
            finally
            {
                await LeaveGate(screen);
            }
        }
        finally
        {
            Leave(screen);
        }
    }

    /// <summary>
    /// Waits for the gate for the timer's release. Work that holds the gate past the bound lets go of every due
    /// button as it leaves, so a busy screen is not a failed release: the press is only asked about again later.
    /// </summary>
    private async Task<bool> TakeGate(Screen screen, PressRecord held, CancellationToken bound)
    {
        try
        {
            await screen.Gate.WaitAsync(bound);
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Sandbox {SandboxId} was busy, so a held mouse button is let go of when that work ends",
                held.Request.SandboxId);

            lock (sync)
            {
                if (IsCurrent(screen, held))
                {
                    Arm(screen, held, limit);
                }
            }

            return false;
        }
    }

    private bool IsDueNow(Screen screen, PressRecord held)
    {
        lock (sync)
        {
            if (closed || !IsCurrent(screen, held))
            {
                return false;
            }

            if (IsDue(held))
            {
                return true;
            }

            Arm(screen, held, Remaining(held));
            return false;
        }
    }

    #endregion

    #region Bookkeeping

    private Screen Enter(Guid spaceId, Guid sandboxId)
    {
        lock (sync)
        {
            if (!screens.TryGetValue((spaceId, sandboxId), out var screen))
            {
                screen = new Screen((spaceId, sandboxId));
                screens[screen.Key] = screen;
            }

            screen.Users++;
            return screen;
        }
    }

    private void Leave(Screen screen)
    {
        lock (sync)
        {
            screen.Users--;
            Discard(screen);
        }
    }

    /// <summary>Drops a screen nobody uses and nothing is held on. Called with the lock held.</summary>
    private void Discard(Screen screen)
    {
        if (screen.Users is 0 && screen.Presses.Count is 0
            && screens.TryGetValue(screen.Key, out var current) && current == screen)
        {
            screens.Remove(screen.Key);
        }
    }

    private bool IsClosed()
    {
        lock (sync)
        {
            return closed;
        }
    }

    private bool IsHeld(Screen screen)
    {
        lock (sync)
        {
            return screen.Presses.Count > 0;
        }
    }

    private static bool IsStuck(Screen screen) => screen.Presses.Values.Any(held => held.Stuck);

    private static bool IsCurrent(Screen screen, PressRecord held) =>
        screen.Presses.TryGetValue(held.Request.Button, out var current) && current == held;

    private Task[] Running()
    {
        lock (sync)
        {
            return [.. expiring];
        }
    }

    /// <summary>
    /// Stops admitting presses, and takes what a stop waits for in the same step: the releases the time limit
    /// started, and every screen with a button held, each counted as in use until the stop is done with it. Null when
    /// the stop already ran.
    /// </summary>
    private StopWork? Close()
    {
        lock (sync)
        {
            if (closed)
            {
                return null;
            }

            closed = true;

            // Every screen, not only those with a button held: a press may be acquiring its sandbox on one, and it
            // registers nothing once the stop has begun.
            Screen[] held = [.. screens.Values];

            foreach (var screen in held)
            {
                screen.Users++;
            }

            return new StopWork([.. expiring], held);
        }
    }

    private PressRecord[] StillHeld()
    {
        lock (sync)
        {
            return [.. screens.Values.SelectMany(screen => screen.Presses.Values)];
        }
    }

    private PressRecord[] Snapshot(Screen screen)
    {
        lock (sync)
        {
            return [.. screen.Presses.Values];
        }
    }

    /// <summary>Forgets this press, and only this one. Called with the lock held.</summary>
    private static void Drop(Screen screen, PressRecord held)
    {
        if (IsCurrent(screen, held))
        {
            held.Timer?.Dispose();
            screen.Presses.Remove(held.Request.Button);
        }
    }

    /// <summary>Forgets the button's press, whichever it is. Called with the lock held.</summary>
    private static void Remove(Screen screen, MouseButton button)
    {
        if (screen.Presses.Remove(button, out var held))
        {
            held.Timer?.Dispose();
        }
    }

    #endregion

    #region Nested Types

    private sealed record StopWork(Task[] Running, Screen[] Screens);

    private sealed class Screen((Guid SpaceId, Guid SandboxId) key)
    {
        #region Properties

        public (Guid SpaceId, Guid SandboxId) Key { get; } = key;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Dictionary<MouseButton, PressRecord> Presses { get; } = [];

        public int Users { get; set; }

        #endregion
    }

    /// <summary>
    /// One press as it is held: when it first went down and when its session last kept it, both on the monotonic
    /// clock, the timer that asks for a check, and how many releases of it failed in a row.
    /// </summary>
    private sealed class PressRecord(PointerPress request, long pressedAt)
    {
        #region Properties

        public PointerPress Request { get; } = request;

        public long PressedAt { get; } = pressedAt;

        public long IdleFrom { get; set; } = pressedAt;

        public ITimer? Timer { get; set; }

        public int Failures { get; set; }

        public long? FailedAt { get; set; }

        public bool Stuck { get; set; }

        #endregion
    }

    #endregion
}
