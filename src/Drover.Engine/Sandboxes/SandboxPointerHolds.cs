namespace Drover.Engine.Sandboxes;

/// <summary>
/// What a press read before it acquired its sandbox: the power counter, and when. A sample too old for the power
/// controls to be remembered is refused, so a press can never be admitted on a record that has been forgotten.
/// </summary>
internal readonly record struct PowerSample(int Epoch, DateTimeOffset At);


/// <summary>The session of a person that pressed a button, which alone may keep it held.</summary>
internal sealed record PointerOwner(Guid UserId, Guid SessionId);


/// <summary>One press of a mouse button on one physical sandbox, and who made it through which bot.</summary>
internal sealed record PointerPress(Guid SpaceId, Guid SandboxId, Guid BotId, PointerOwner Owner, MouseButton Button);


/// <summary>
/// The mouse buttons people hold down on sandbox screens, so that every one is let go of. A held button is a state of
/// a physical display that no connection owns: a person whose connection is lost, or whose request fails or is
/// cancelled, would leave it down for the next person or bot to find.
/// <para>
/// A hold belongs to the physical sandbox instance, not to the bot that was asked: bots that share a sandbox share its
/// display, so they share its holds. It belongs to the session that pressed the button, which alone may keep it held
/// by moving, and it ends after <see cref="MaxHold"/> whatever is done. Presses, releases and the lifting of a button
/// on one sandbox take turns (one gate for each), and a release is tied to the press it was made for, so cleanup of an
/// old press never erases a newer one.
/// </para>
/// <para>
/// A button is let go of when the request that pressed it fails or is cancelled, when <see cref="Limit"/> passes with
/// no move from the session that pressed it, when the sandbox is paused, rebuilt or turned off from the screen, and
/// when the server stops. A release that fails is tried again after the same time, and after
/// <see cref="MaxFailedReleases"/> failures in a row the screen takes no input until the button is let go of or the
/// display is reset (the sandbox rebuilt or turned off), since the display may still have the button down. Nothing
/// here keeps a position: only which buttons are down, for which sandbox, and by whom.
/// </para>
/// </summary>
internal sealed class SandboxPointerHolds(
    TimeProvider time,
    ILogger logger,
    Func<PointerPress, CancellationToken, Task<ReleaseOutcome>> release) : IAsyncDisposable
{
    #region Private Fields

    private const int MaxFailedReleases = 6;

    private const string StuckWhy = "A mouse button on this screen could not be let go of, so it takes no input until";

    private const string StuckWhat = "it is. Let go of the button, or rebuild the sandbox.";

    private const string StuckSentence = $"{StuckWhy} {StuckWhat}";

    private static readonly TimeSpan limit = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan maxHold = TimeSpan.FromSeconds(60);

    /// <summary>How long a power control is remembered: longer than any press can wait to be admitted.</summary>
    private static readonly TimeSpan powerMemory = TimeSpan.FromMinutes(15);

    private readonly Lock sync = new();

    private readonly Dictionary<(Guid SpaceId, Guid SandboxId), Screen> screens = [];

    private readonly List<Task> expiring = [];

    private bool closed;

    private int powerEpoch;

    /// <summary>The power controls that acted, by the space and the physical sandbox they acted on, and when.</summary>
    private readonly Dictionary<(Guid SpaceId, Guid SandboxId), (int Epoch, DateTimeOffset At)> powered = [];

    #endregion

    #region Properties

    /// <summary>
    /// Runs when a press finds the screen's gate taken and is about to wait for it. A test uses it to know the press
    /// has reached the gate. Not used outside tests.
    /// </summary>
    internal Action? PressWaiting { get; set; }

    /// <summary>
    /// A number that grows each time a power control (pause, rebuild, turn off) acts on any sandbox. A press reads it
    /// before it acquires its sandbox and gives it back to <see cref="Press"/>, which refuses the press only if a
    /// control acted on that press's own sandbox in its own space after the number was read: a control that ran
    /// while the press held a sandbox that may since have been paused or replaced, even one that failed or was
    /// cancelled after the provider acted. A control on another sandbox or in another space changes the number and
    /// nothing else, so one tenant's activity never ends another's press or drag.
    /// </summary>
    public PowerSample PowerEpoch => new(Volatile.Read(ref powerEpoch), time.GetUtcNow());

    /// <summary>How long a held button may go without a move from the session that pressed it.</summary>
    public static TimeSpan Limit => limit;

    /// <summary>The longest a button is held, however often its session moves.</summary>
    public static TimeSpan MaxHold => maxHold;

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs the press <paramref name="run"/> makes, with the button noted before it runs, so that a press that fails or
    /// is cancelled halfway has its button let go of before the exception goes on. A press given the
    /// <paramref name="epoch"/> it read before it acquired its sandbox is refused when a power control ran since. With
    /// <paramref name="staysDown"/> off (a drag, which lets go by itself) the button is forgotten once it worked.
    /// </summary>
    public async Task Press(
        PointerPress press,
        bool staysDown,
        Func<Task> run,
        CancellationToken cancellationToken,
        PowerSample? epoch = null)
    {
        RefuseWhenStopping();
        var screen = Enter(press.SpaceId, press.SandboxId);

        try
        {
            if (screen.Gate.CurrentCount is 0)
            {
                PressWaiting?.Invoke();
            }

            await screen.Gate.WaitAsync(cancellationToken);

            try
            {
                var held = Register(screen, press, epoch);
                await RunPressed(screen, held, run);
                await Settle(screen, held, staysDown);
            }
            finally
            {
                screen.Gate.Release();
            }
        }
        finally
        {
            Leave(press.SpaceId, press.SandboxId, screen);
        }
    }

    /// <summary>
    /// Runs a move. A button held by this session stays held for another <see cref="Limit"/>, as far as
    /// <see cref="MaxHold"/> allows; another session's moves keep nothing.
    /// </summary>
    public async Task Move(Guid spaceId, Guid sandboxId, PointerOwner owner, Func<Task> run)
    {
        await Guard(spaceId, sandboxId, run);
        Renew(spaceId, sandboxId, owner);
    }

    /// <summary>Runs an input that needs a display that has no stuck button: it is refused when one is.</summary>
    public Task Guard(Guid spaceId, Guid sandboxId, Func<Task> run)
    {
        lock (sync)
        {
            if (screens.TryGetValue((spaceId, sandboxId), out var screen) && IsStuck(screen))
            {
                throw new BuiltinToolException(StuckSentence);
            }
        }

        return run();
    }

    /// <summary>
    /// Runs the release of a button and forgets it once it worked, whoever pressed it: letting go is never refused.
    /// This is how a button that could not be let go of by the server is let go of by a person.
    /// </summary>
    public async Task Lift(
        Guid spaceId,
        Guid sandboxId,
        MouseButton button,
        Func<Task> run,
        CancellationToken cancellationToken)
    {
        var screen = Enter(spaceId, sandboxId);

        try
        {
            await screen.Gate.WaitAsync(cancellationToken);

            try
            {
                await run();

                lock (sync)
                {
                    Remove(screen, button);
                }
            }
            finally
            {
                screen.Gate.Release();
            }
        }
        finally
        {
            Leave(spaceId, sandboxId, screen);
        }
    }

    /// <summary>
    /// Lets go of every button held on this sandbox and waits for it. True when none is held any more. A release that
    /// fails leaves the button held, and the time limit starts again.
    /// </summary>
    public async Task<bool> ReleaseAll(Guid spaceId, Guid sandboxId, CancellationToken cancellationToken)
    {
        var screen = Enter(spaceId, sandboxId);

        try
        {
            await screen.Gate.WaitAsync(cancellationToken);

            try
            {
                foreach (var held in Snapshot(screen))
                {
                    await ReleaseHeld(screen, held, cancellationToken);
                }
            }
            finally
            {
                screen.Gate.Release();
            }

            lock (sync)
            {
                return screen.Presses.Count is 0;
            }
        }
        finally
        {
            Leave(spaceId, sandboxId, screen);
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
            Discard(spaceId, sandboxId, screen);
        }
    }

    /// <summary>
    /// Stops taking presses, waits for the releases that are running and for the presses that are, then lets go of
    /// every button still held, all within one budget of ten seconds. A press that is still running when the budget is
    /// spent lets go of its own button when it ends, since the server has stopped, so none is left down.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var (running, held) = Close();
        using var budget = new CancellationTokenSource(limit, time);

        // Every screen is tried on its own, all under the one budget, so a busy or unreachable screen cannot spend it
        // before the others are attempted.
        List<Task> cleanups = [.. held.Select(screen => ReleaseOnStop(screen, budget.Token))];
        await Task.WhenAll(cleanups);

        try
        {
            await Task.WhenAll(running).WaitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Releases were still running after {Seconds} seconds as the server stopped",
                limit.TotalSeconds);
        }

        lock (sync)
        {
            foreach (var press in screens.Values.SelectMany(screen => screen.Presses.Values))
            {
                press.Timer?.Dispose();
            }
        }
    }

    /// <summary>
    /// Lets go of every button held on this sandbox and, with the screen's gate still held so that no press or
    /// release can come between, runs <paramref name="work"/>. False, and the work is not run, when
    /// <paramref name="required"/> is set and a button could not be let go of. This is how a pause is made to take
    /// effect on a display with nothing held down.
    /// </summary>
    public async Task<bool> RunWhileReleased(
        Guid spaceId,
        Guid sandboxId,
        bool required,
        Func<Task> work,
        CancellationToken cancellationToken)
    {
        var screen = Enter(spaceId, sandboxId);
        var acted = false;

        try
        {
            await screen.Gate.WaitAsync(cancellationToken);

            try
            {
                foreach (var held in Snapshot(screen))
                {
                    await ReleaseHeld(screen, held, cancellationToken);
                }

                if (required && IsHeld(screen))
                {
                    return false;
                }

                // The guarded transition begins here, inside the gate: a control that was cancelled while it waited
                // or while it let go of a button, or refused because a button could not be let go of, ran nothing
                // and invalidates nothing.
                cancellationToken.ThrowIfCancellationRequested();
                MarkPower(spaceId, sandboxId);
                acted = true;
                await work();
                return true;
            }
            finally
            {
                if (acted)
                {
                    // However it ended: the provider may have acted before a failure or a cancellation.
                    MarkPower(spaceId, sandboxId);
                }

                screen.Gate.Release();
            }
        }
        finally
        {
            Leave(spaceId, sandboxId, screen);
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
                throw new BuiltinToolException("This server is stopping, so nothing was pressed. Try again soon.");
            }
        }
    }

    private PressRecord Register(Screen screen, PointerPress press, PowerSample? epoch)
    {
        RefuseWhenStopping();

        lock (sync)
        {
            if (epoch is { } sampled && time.GetUtcNow() - sampled.At > powerMemory)
            {
                // The marks a press that old could need may be forgotten, so it cannot be told nothing happened.
                throw new BuiltinToolException(
                    "This waited too long to be sure the sandbox was unchanged, so nothing was pressed. Try again.");
            }

            if (epoch is { } read
                && powered.TryGetValue((press.SpaceId, press.SandboxId), out var power)
                && power.Epoch > read.Epoch)
            {
                throw new BuiltinToolException(
                    "The sandbox was paused or replaced while this waited, so nothing was pressed. Try again.");
            }

            if (IsStuck(screen))
            {
                throw new BuiltinToolException(StuckSentence);
            }

            if (screen.Presses.TryGetValue(press.Button, out var existing))
            {
                if (existing.Request.Owner != press.Owner)
                {
                    throw new BuiltinToolException("Another person is holding that mouse button on this screen.");
                }

                // The same physical press: its longest hold counts from when the button went down, not from now.
                existing.Timer?.Dispose();
                return existing;
            }

            var held = new PressRecord(press, time.GetUtcNow() + maxHold);
            screen.Presses[press.Button] = held;
            return held;
        }
    }

    private async Task RunPressed(Screen screen, PressRecord held, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception)
        {
            await ReleaseHeld(screen, held, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The press worked: it is held with its time limit started, or, for a drag, it is already over. When the server
    /// stopped while it ran, the button is let go of here, under the gate the press still holds, since the stop can no
    /// longer wait for it.
    /// </summary>
    private async Task Settle(Screen screen, PressRecord held, bool staysDown)
    {
        lock (sync)
        {
            if (!IsCurrent(screen, held))
            {
                return;
            }

            if (!staysDown)
            {
                Remove(screen, held.Request.Button);
                return;
            }

            if (!closed)
            {
                Arm(screen, held, Within(held));
                return;
            }
        }

        using var bound = new CancellationTokenSource(limit, time);

        try
        {
            await ReleaseHeld(screen, held, bound.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "A press that ended as the server stopped could not be let go of within {Seconds} seconds",
                limit.TotalSeconds);
        }
    }

    private void Renew(Guid spaceId, Guid sandboxId, PointerOwner owner)
    {
        lock (sync)
        {
            if (!screens.TryGetValue((spaceId, sandboxId), out var screen))
            {
                return;
            }

            foreach (var held in screen.Presses.Values.Where(held => held.Request.Owner == owner && !held.Stuck))
            {
                Arm(screen, held, Within(held));
            }
        }
    }

    #endregion

    #region Letting Go

    /// <summary>
    /// Lets go of one press, called with the screen's gate held. The press is forgotten only when it worked, or the
    /// sandbox is known to be out of use; otherwise it stays held and its limit starts again, until it has failed too
    /// many times in a row and the screen takes no input.
    /// </summary>
    private async Task<bool> ReleaseHeld(Screen screen, PressRecord held, CancellationToken cancellationToken)
    {
        try
        {
            if (await release(held.Request, cancellationToken) is ReleaseOutcome.Released or ReleaseOutcome.Gone)
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
            // The caller ended the request: that is not a failure of the release, which is tried again.
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

    private void Fail(Screen screen, PressRecord held)
    {
        lock (sync)
        {
            if (!IsCurrent(screen, held))
            {
                return;
            }

            held.Failures++;

            if (held.Failures >= MaxFailedReleases)
            {
                logger.LogError(
                    "A mouse button on sandbox {SandboxId} could not be let go of after {Attempts} tries",
                    held.Request.SandboxId,
                    held.Failures);
                held.Stuck = true;
                held.Timer?.Dispose();
                return;
            }

            Arm(screen, held, limit);
        }
    }

    /// <summary>
    /// Lets go of what is held on one screen as the server stops, within the stop's budget. The budget running out
    /// ends this screen's attempt and is logged; it never ends another screen's.
    /// </summary>
    private async Task ReleaseOnStop(Screen screen, CancellationToken budget)
    {
        try
        {
            await screen.Gate.WaitAsync(budget);

            try
            {
                foreach (var held in Snapshot(screen))
                {
                    await ReleaseHeld(screen, held, budget);
                }
            }
            finally
            {
                screen.Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "A screen was still busy after {Seconds} seconds as the server stopped, with a button held on it",
                limit.TotalSeconds);
        }
    }

    #endregion

    #region The Time Limit

    /// <summary>Starts, or starts again, the time for the press. Called with the lock held.</summary>
    private void Arm(Screen screen, PressRecord held, TimeSpan due)
    {
        held.Timer?.Dispose();

        if (closed)
        {
            return;
        }

        held.Generation++;
        var generation = held.Generation;
        held.Timer = time.CreateTimer(_ => Expire(screen, held, generation), null, due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The time to the next limit for a press: <see cref="Limit"/>, and never past its longest life.</summary>
    private TimeSpan Within(PressRecord held) =>
        TimeSpan.FromTicks(Math.Clamp((held.MaxAt - time.GetUtcNow()).Ticks, 0, limit.Ticks));

    /// <summary>
    /// The timer set for this press and this renewal ran out: the press is let go of if it is still the one it was
    /// set for.
    /// </summary>
    private void Expire(Screen screen, PressRecord held, int generation)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (sync)
        {
            if (closed || held.Generation != generation || held.Stuck || !IsCurrent(screen, held))
            {
                return;
            }

            // Admitted and registered in one step, so a stop that closes after this sees the work and waits for it.
            screen.Users++;
            expiring.Add(done.Task);
        }

        _ = RunExpiry(screen, held, generation, done);
    }

    private async Task RunExpiry(Screen screen, PressRecord held, int generation, TaskCompletionSource done)
    {
        try
        {
            logger.LogInformation(
                "A mouse button held on sandbox {SandboxId} is being let go of after {Seconds} seconds",
                held.Request.SandboxId,
                limit.TotalSeconds);
            await ReleaseExpired(screen, held, generation);
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

    private async Task ReleaseExpired(Screen screen, PressRecord held, int generation)
    {
        using var bound = new CancellationTokenSource(limit, time);

        try
        {
            await screen.Gate.WaitAsync(bound.Token);

            try
            {
                if (StillDue(screen, held, generation))
                {
                    await ReleaseHeld(screen, held, bound.Token);
                }
            }
            finally
            {
                screen.Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Sandbox {SandboxId} was busy, so a held mouse button waits for its next try",
                held.Request.SandboxId);
            Fail(screen, held);
        }
        finally
        {
            Leave(held.Request.SpaceId, held.Request.SandboxId, screen);
        }
    }

    /// <summary>The press is still the one the timer was set for, with the time it was set for.</summary>
    private bool StillDue(Screen screen, PressRecord held, int generation)
    {
        lock (sync)
        {
            return !closed && held.Generation == generation && !held.Stuck && IsCurrent(screen, held);
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
                screen = new Screen();
                screens[(spaceId, sandboxId)] = screen;
            }

            screen.Users++;
            return screen;
        }
    }

    private void Leave(Guid spaceId, Guid sandboxId, Screen screen)
    {
        lock (sync)
        {
            screen.Users--;
            Discard(spaceId, sandboxId, screen);
        }
    }

    /// <summary>Drops a screen nobody uses and nothing is held on. Called with the lock held.</summary>
    private void Discard(Guid spaceId, Guid sandboxId, Screen screen)
    {
        if (screen.Users is 0 && screen.Presses.Count is 0)
        {
            screens.Remove((spaceId, sandboxId));
        }
    }

    /// <summary>
    /// Records that a power control acted on this sandbox, and forgets the ones too old for any press to still be
    /// waiting on. Only the sandbox's own space and id are marked.
    /// </summary>
    private void MarkPower(Guid spaceId, Guid sandboxId)
    {
        lock (sync)
        {
            var now = time.GetUtcNow();
            var epoch = Interlocked.Increment(ref powerEpoch);
            powered[(spaceId, sandboxId)] = (epoch, now);

            List<(Guid, Guid)> forgotten =
            [
                .. powered.Where(entry => now - entry.Value.At > powerMemory).Select(entry => entry.Key),
            ];

            foreach (var key in forgotten)
            {
                powered.Remove(key);
            }
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

    private bool IsCurrent(Screen screen, PressRecord held) =>
        screen.Presses.TryGetValue(held.Request.Button, out var current) && current.Id == held.Id;

    private Task[] Running()
    {
        lock (sync)
        {
            return [.. expiring];
        }
    }

    /// <summary>Stops admitting work, and takes what a stop waits for in the same step.</summary>
    private (Task[] Running, Screen[] Screens) Close()
    {
        lock (sync)
        {
            closed = true;
            return ([.. expiring], [.. screens.Values]);
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
    private void Drop(Screen screen, PressRecord held)
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

    private sealed class Screen
    {
        #region Properties

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Dictionary<MouseButton, PressRecord> Presses { get; } = [];

        public int Users { get; set; }

        #endregion
    }

    private sealed class PressRecord(PointerPress request, DateTimeOffset maxAt)
    {
        #region Properties

        public Guid Id { get; } = Guid.CreateVersion7();

        public PointerPress Request { get; } = request;

        public DateTimeOffset MaxAt { get; } = maxAt;

        public ITimer? Timer { get; set; }

        public int Generation { get; set; }

        public int Failures { get; set; }

        public bool Stuck { get; set; }

        #endregion
    }

    #endregion
}
