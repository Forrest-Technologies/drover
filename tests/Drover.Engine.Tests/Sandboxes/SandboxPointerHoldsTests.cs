namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// What is held down on a sandbox's display, in memory with a clock a test moves and a release a test scripts: whose a
/// held button is, how long it is held, that cleanup is tied to the press it was made for, that the time limit and the
/// stop of the server take turns with presses, and that a button that cannot be let go of leaves the screen without
/// input until it is.
/// </summary>
public sealed class SandboxPointerHoldsTests
{
    #region Whose A Hold Is

    [Fact]
    public async Task A_button_nobody_moves_is_let_go_of_after_the_limit_and_not_before()
    {
        var world = new World();
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        world.Clock.Advance(SandboxPointerHolds.Limit - TimeSpan.FromSeconds(1));
        world.Calls.Should().BeEmpty();
        world.Clock.Advance(TimeSpan.FromSeconds(1));

        world.Calls.Should().ContainSingle().Which.Button.Should().Be(MouseButton.Left);
    }

    [Fact]
    public async Task A_move_keeps_a_button_only_for_the_session_that_pressed_it()
    {
        var world = new World();
        var presser = World.Owner(1);
        var another = World.Owner(2);
        await world.Holds.Press(world.Press(presser), staysDown: true, World.Nothing, World.Token);

        world.Clock.Advance(TimeSpan.FromSeconds(9));
        await world.Holds.Move(World.Space, world.Sandbox, another, World.Nothing);
        world.Clock.Advance(TimeSpan.FromSeconds(1));

        world.Calls.Should().ContainSingle("another session's move keeps nothing");

        var kept = new World();
        await kept.Holds.Press(kept.Press(presser), staysDown: true, World.Nothing, World.Token);
        kept.Clock.Advance(TimeSpan.FromSeconds(9));
        await kept.Holds.Move(World.Space, kept.Sandbox, presser, World.Nothing);
        kept.Clock.Advance(TimeSpan.FromSeconds(1));

        kept.Calls.Should().BeEmpty("the session that pressed it kept it");
    }

    [Fact]
    public async Task The_same_person_in_another_session_does_not_keep_the_button_either()
    {
        var world = new World();
        var userId = Guid.CreateVersion7();
        await world.Holds.Press(world.Press(new PointerOwner(userId, Guid.CreateVersion7())), staysDown: true,
            World.Nothing, World.Token);

        world.Clock.Advance(TimeSpan.FromSeconds(9));
        await world.Holds.Move(World.Space, world.Sandbox, new PointerOwner(userId, Guid.CreateVersion7()),
            World.Nothing);
        world.Clock.Advance(TimeSpan.FromSeconds(1));

        world.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task A_button_is_let_go_of_after_its_longest_hold_however_often_it_is_kept()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);

        for (var second = 9; second <= 54; second += 9)
        {
            world.Clock.Advance(TimeSpan.FromSeconds(9));
            await world.Holds.Move(World.Space, world.Sandbox, owner, World.Nothing);
        }

        world.Calls.Should().BeEmpty("it was kept every nine seconds so far");
        world.Clock.Advance(TimeSpan.FromSeconds(5));
        await world.Holds.Move(World.Space, world.Sandbox, owner, World.Nothing);
        world.Calls.Should().BeEmpty();
        world.Clock.Advance(TimeSpan.FromSeconds(1));

        world.Calls.Should().ContainSingle("sixty seconds after the press, whatever was done since");
        SandboxPointerHolds.MaxHold.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_button_held_by_one_person_is_refused_to_another_through_any_bot_of_the_same_sandbox()
    {
        var world = new World();
        await world.Holds.Press(world.Press(World.Owner(1), bot: Guid.CreateVersion7()), staysDown: true, World.Nothing,
            World.Token);
        var ran = false;

        var another = () => world.Holds.Press(
            world.Press(World.Owner(2), bot: Guid.CreateVersion7()),
            staysDown: true,
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            World.Token);

        await another.Should().ThrowAsync<BuiltinToolException>().WithMessage("*Another person*");
        ran.Should().BeFalse("nothing was pressed");
        await world.Holds.Press(world.Press(World.Owner(2), button: MouseButton.Right), staysDown: true, World.Nothing,
            World.Token);
    }

    [Fact]
    public async Task The_same_session_may_press_a_held_button_again_and_a_different_sandbox_has_its_own_holds()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);
        var elsewhere = world.Press(World.Owner(2)) with { SandboxId = Guid.CreateVersion7() };

        await world.Holds.Press(elsewhere, staysDown: true, World.Nothing, World.Token);
        world.Clock.Advance(SandboxPointerHolds.Limit);

        world.Calls.Select(call => call.SandboxId).Should().BeEquivalentTo([world.Sandbox, elsewhere.SandboxId]);
    }

    [Fact]
    public async Task A_release_is_for_the_sandbox_and_the_button_that_was_pressed()
    {
        var world = new World();
        await world.Holds.Press(world.Press(button: MouseButton.Middle), staysDown: true, World.Nothing, World.Token);

        world.Clock.Advance(SandboxPointerHolds.Limit);

        var call = world.Calls.Should().ContainSingle().Subject;
        call.Should().Match<PointerPress>(held => held.Button == MouseButton.Middle && held.SandboxId == world.Sandbox);
    }

    #endregion

    #region Cleanup Is Tied To A Press

    [Fact]
    public async Task A_release_that_finishes_after_a_newer_press_does_not_erase_it()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);
        var first = world.BlockNextRelease();
        world.Clock.Advance(SandboxPointerHolds.Limit);
        await first.Started;
        var pressedAgain = false;

        var second = world.Holds.Press(
            world.Press(owner),
            staysDown: true,
            () =>
            {
                pressedAgain = true;
                return Task.CompletedTask;
            },
            World.Token);
        await World.Idle();

        pressedAgain.Should().BeFalse("a press and a release of one sandbox take turns");
        first.Finish(ReleaseOutcome.Released);
        await second;
        pressedAgain.Should().BeTrue();
        world.Clock.Advance(SandboxPointerHolds.Limit);

        world.Calls.Should().HaveCount(2, "the newer press is held and is let go of in its turn");
    }

    [Fact]
    public async Task A_press_that_fails_is_let_go_of_before_the_exception_goes_on_and_only_that_press()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner, button: MouseButton.Right), staysDown: true, World.Nothing,
            World.Token);

        var failing = () => world.Holds.Press(world.Press(owner), staysDown: true,
            () => throw new InvalidOperationException(), World.Token);

        await failing.Should().ThrowAsync<InvalidOperationException>();
        world.Calls.Should().ContainSingle().Which.Button.Should().Be(MouseButton.Left);
        world.Clock.Advance(SandboxPointerHolds.Limit);
        world.Calls.Select(call => call.Button).Should().Equal(MouseButton.Left, MouseButton.Right);
    }

    [Fact]
    public async Task A_drag_that_worked_leaves_nothing_held_and_one_that_failed_is_let_go_of()
    {
        var world = new World();
        await world.Holds.Press(world.Press(), staysDown: false, World.Nothing, World.Token);
        world.Clock.Advance(SandboxPointerHolds.Limit * 2);
        world.Calls.Should().BeEmpty();

        var failing = () => world.Holds.Press(world.Press(), staysDown: false,
            () => throw new InvalidOperationException(), World.Token);

        await failing.Should().ThrowAsync<InvalidOperationException>();
        world.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task A_timer_that_was_set_before_a_renewal_does_not_let_go_of_the_renewed_button()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);
        var busy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressingRight = world.Holds.Press(
            world.Press(owner, button: MouseButton.Right),
            staysDown: true,
            () => busy.Task,
            World.Token);

        world.Clock.Advance(SandboxPointerHolds.Limit);
        await world.Holds.Move(World.Space, world.Sandbox, owner, World.Nothing);
        busy.SetResult();
        await pressingRight;
        await World.Idle();

        world.Calls.Should().BeEmpty("the left button was kept after the timer that waits for the gate was set");
        world.Clock.Advance(SandboxPointerHolds.Limit);
        world.Calls.Select(call => call.Button).Should().BeEquivalentTo([MouseButton.Left, MouseButton.Right]);
    }

    [Fact]
    public async Task A_release_of_the_button_lets_go_of_it_for_anyone_and_forgets_it()
    {
        var world = new World();
        await world.Holds.Press(world.Press(World.Owner(1)), staysDown: true, World.Nothing, World.Token);

        await world.Holds.Lift(World.Space, world.Sandbox, MouseButton.Left, World.Nothing, World.Token);
        world.Clock.Advance(SandboxPointerHolds.Limit * 2);

        world.Calls.Should().BeEmpty();
        await world.Holds.Press(world.Press(World.Owner(2)), staysDown: true, World.Nothing, World.Token);
    }

    [Fact]
    public async Task A_release_that_fails_leaves_the_button_held()
    {
        var world = new World();
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        var lifting = () => world.Holds.Lift(World.Space, world.Sandbox, MouseButton.Left,
            () => throw new InvalidOperationException(), World.Token);

        await lifting.Should().ThrowAsync<InvalidOperationException>();
        world.Clock.Advance(SandboxPointerHolds.Limit);
        world.Calls.Should().ContainSingle("it is still held, so the time limit lets go of it");
    }

    #endregion

    #region Stopping

    [Fact]
    public async Task Stopping_waits_for_a_release_that_is_running_and_does_not_let_go_of_the_button_twice()
    {
        var world = new World();
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);
        var running = world.BlockNextRelease();
        world.Clock.Advance(SandboxPointerHolds.Limit);
        await running.Started;

        var stopping = world.Holds.DisposeAsync().AsTask();
        await World.Idle();

        stopping.IsCompleted.Should().BeFalse("a release is running for this button");
        running.Finish(ReleaseOutcome.Released);
        await stopping;
        world.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Stopping_drains_a_press_that_is_running_and_lets_go_of_what_it_pressed_and_refuses_new_presses()
    {
        var world = new World();
        var inFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressing = world.Holds.Press(world.Press(), staysDown: true, () => inFlight.Task, World.Token);

        var stopping = world.Holds.DisposeAsync().AsTask();
        await World.Idle();
        var late = () => world.Holds.Press(world.Press(World.Owner(2), button: MouseButton.Right), staysDown: true,
            World.Nothing, World.Token);

        await late.Should().ThrowAsync<BuiltinToolException>().WithMessage("*stopping*");
        stopping.IsCompleted.Should().BeFalse("a press is still running, and its button is not yet held");
        inFlight.SetResult();
        await pressing;
        await stopping;

        world.Calls.Should().ContainSingle().Which.Button.Should().Be(MouseButton.Left);
    }

    [Fact]
    public async Task Stopping_lets_go_of_every_sandboxs_buttons()
    {
        var world = new World();
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);
        await world.Holds.Press(world.Press() with { SandboxId = Guid.CreateVersion7() }, staysDown: true,
            World.Nothing, World.Token);

        await world.Holds.DisposeAsync();

        world.Calls.Should().HaveCount(2);
    }

    #endregion

    #region The Same Press And The Stop

    [Fact]
    public async Task Work_started_by_the_timer_is_registered_before_anything_else_is_done_for_it()
    {
        var world = new World();
        var registered = new List<bool>();
        world.Watching = message =>
        {
            if (message.Contains("is being let go of", StringComparison.Ordinal))
            {
                registered.Add(!world.Holds.Quiet().IsCompleted);
            }
        };
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        world.Clock.Advance(SandboxPointerHolds.Limit);
        await world.Holds.Quiet();

        registered.Should().Equal([true], "a stop that closes at that moment must see the work and wait for it");
    }

    [Fact]
    public async Task A_second_press_of_a_held_button_does_not_restart_its_longest_hold()
    {
        var world = new World();
        var owner = World.Owner(1);
        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);

        for (var step = 0; step < 5; step++)
        {
            world.Clock.Advance(TimeSpan.FromSeconds(9));
            await world.Holds.Move(World.Space, world.Sandbox, owner, World.Nothing);
        }

        await world.Holds.Press(world.Press(owner), staysDown: true, World.Nothing, World.Token);
        world.Clock.Advance(TimeSpan.FromSeconds(9));
        await world.Holds.Move(World.Space, world.Sandbox, owner, World.Nothing);
        world.Calls.Should().BeEmpty("forty-five seconds in, the button was kept every nine seconds");
        world.Clock.Advance(TimeSpan.FromSeconds(6));
        await world.Holds.Quiet();

        world.Calls.Should().ContainSingle("sixty seconds after the first press, not after the second");
    }

    [Fact]
    public async Task A_press_still_running_when_the_budget_of_a_stop_is_spent_lets_go_of_its_button_when_it_ends()
    {
        var world = new World();
        var inFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressing = world.Holds.Press(world.Press(), staysDown: true, () => inFlight.Task, World.Token);
        var stopping = world.Holds.DisposeAsync().AsTask();
        await World.Idle();
        stopping.IsCompleted.Should().BeFalse("the press is still running");

        world.Clock.Advance(SandboxPointerHolds.Limit);
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), World.Token);
        world.Calls.Should().BeEmpty("the press had not ended, so there was nothing to let go of yet");
        inFlight.SetResult();
        await pressing;

        world.Calls.Should().ContainSingle().Which.Button.Should().Be(MouseButton.Left);
        world.Clock.Advance(SandboxPointerHolds.Limit * 3);
        world.Calls.Should().ContainSingle("it was let go of once, and nothing is left to expire");
    }

    [Fact]
    public async Task A_stop_tries_every_sandbox_on_its_own_under_one_budget_so_a_busy_one_does_not_spend_it()
    {
        var world = new World();
        var busy = world.Sandbox;
        var other = Guid.CreateVersion7();
        world.Behaviour = (held, token) => held.SandboxId == busy
            ? World.Wait(token)
            : Task.FromResult(ReleaseOutcome.Released);
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);
        await world.Holds.Press(world.Press() with { SandboxId = other }, staysDown: true, World.Nothing, World.Token);

        var stopping = world.Holds.DisposeAsync().AsTask();
        await World.Idle();

        world.Calls.Select(call => call.SandboxId).Should().BeEquivalentTo(
            [busy, other],
            "the second sandbox was tried while the first was still busy");
        stopping.IsCompleted.Should().BeFalse("the busy sandbox is still within the budget");
        world.Clock.Advance(SandboxPointerHolds.Limit);

        await stopping.WaitAsync(TimeSpan.FromSeconds(10), World.Token);
        world.Calls.Should().HaveCount(2, "the budget ended the busy one, and nothing was given another ten seconds");
    }

    [Fact]
    public async Task A_press_that_waited_for_a_pause_to_run_is_refused_and_registers_nothing()
    {
        var world = new World();
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Holds.PressWaiting = () => waiting.TrySetResult();
        var pausing = world.Holds.RunWhileReleased(
            World.Space,
            world.Sandbox,
            required: true,
            async () =>
            {
                inside.SetResult();
                await finish.Task;
            },
            World.Token);
        await inside.Task;
        var ran = false;

        var press = world.Holds.Press(
            world.Press(),
            staysDown: true,
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            World.Token);
        await waiting.Task;
        finish.SetResult();
        await pausing;
        var refused = async () => await press;

        await refused.Should().ThrowAsync<BuiltinToolException>().WithMessage("*paused or replaced*");
        ran.Should().BeFalse("nothing was pressed on a display that was paused under it");
        world.Clock.Advance(SandboxPointerHolds.Limit * 3);
        world.Calls.Should().BeEmpty("nothing was registered, so nothing is left to let go of");
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);
    }

    [Fact]
    public async Task A_pause_runs_with_nothing_held_and_no_press_can_come_between_the_release_and_the_pause()
    {
        var world = new World();
        await world.Holds.Press(world.Press(World.Owner(1)), staysDown: true, World.Nothing, World.Token);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Holds.PressWaiting = () => waiting.TrySetResult();

        var pausing = world.Holds.RunWhileReleased(
            World.Space,
            world.Sandbox,
            required: true,
            async () =>
            {
                inside.SetResult();
                await finish.Task;
            },
            World.Token);
        await inside.Task;
        var pressed = false;
        var press = world.Holds.Press(
            world.Press(World.Owner(2), button: MouseButton.Right),
            staysDown: true,
            () =>
            {
                pressed = true;
                return Task.CompletedTask;
            },
            World.Token);
        await waiting.Task;

        world.Calls.Should().ContainSingle("the held button was let go of before the pause began");
        pressed.Should().BeFalse("a press waits for the pause to end");
        finish.SetResult();
        (await pausing).Should().BeTrue();
        var refused = async () => await press;

        await refused.Should().ThrowAsync<BuiltinToolException>().WithMessage("*paused or replaced*");
        pressed.Should().BeFalse("the press that waited for the pause was refused, not run on the paused display");
    }

    [Fact]
    public async Task A_pause_does_not_run_while_a_button_cannot_be_let_go_of_when_it_is_required_not_to()
    {
        var world = new World { Outcome = ReleaseOutcome.Unreachable };
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);
        var ran = false;

        var required = await world.Holds.RunWhileReleased(World.Space, world.Sandbox, required: true, () =>
        {
            ran = true;
            return Task.CompletedTask;
        }, World.Token);
        var notRequired = await world.Holds.RunWhileReleased(World.Space, world.Sandbox, required: false, () =>
        {
            ran = true;
            return Task.CompletedTask;
        }, World.Token);

        required.Should().BeFalse();
        notRequired.Should().BeTrue();
        ran.Should().BeTrue("only the work that did not require it ran");
    }

    #endregion

    #region A Button That Cannot Be Let Go Of

    [Theory]
    [InlineData(ReleaseOutcome.Unreachable)]
    [InlineData(ReleaseOutcome.Released)]
    public async Task A_release_not_done_is_tried_again_after_every_limit_and_a_done_one_is_not(ReleaseOutcome outcome)
    {
        var world = new World { Outcome = outcome };
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            world.Clock.Advance(SandboxPointerHolds.Limit);
        }

        world.Calls.Should().HaveCount(outcome is ReleaseOutcome.Unreachable ? 3 : 1);
    }

    [Fact]
    public async Task A_sandbox_that_is_gone_is_not_a_reason_to_keep_the_button_but_one_that_cannot_be_reached_is()
    {
        var gone = new World { Outcome = ReleaseOutcome.Gone };
        await gone.Holds.Press(gone.Press(World.Owner(1)), staysDown: true, World.Nothing, World.Token);
        gone.Clock.Advance(SandboxPointerHolds.Limit);

        await gone.Holds.Press(gone.Press(World.Owner(2)), staysDown: true, World.Nothing, World.Token);

        var unreachable = new World { Outcome = ReleaseOutcome.Unreachable };
        await unreachable.Holds.Press(unreachable.Press(World.Owner(1)), staysDown: true, World.Nothing, World.Token);
        unreachable.Clock.Advance(SandboxPointerHolds.Limit);

        var refused = () => unreachable.Holds.Press(unreachable.Press(World.Owner(2)), staysDown: true, World.Nothing,
            World.Token);
        await refused.Should().ThrowAsync<BuiltinToolException>().WithMessage("*Another person*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task After_the_last_failed_release_the_screen_takes_no_input_until_the_button_is_let_go(bool throwing)
    {
        var world = new World();
        world.Behaviour = (_,
            _) => throwing ? throw new InvalidOperationException() : Task.FromResult(ReleaseOutcome.Unreachable);
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            world.Clock.Advance(SandboxPointerHolds.Limit);
        }

        world.Calls.Should().HaveCount(6, "the sixth failure in a row ends the tries");
        var guarded = () => world.Holds.Guard(World.Space, world.Sandbox, World.Nothing);
        var pressing = () => world.Holds.Press(world.Press(World.Owner(2), button: MouseButton.Right), staysDown: true,
            World.Nothing, World.Token);
        var moving = () => world.Holds.Move(World.Space, world.Sandbox, World.Owner(1), World.Nothing);
        await guarded.Should().ThrowAsync<BuiltinToolException>().WithMessage("*could not be let go of*");
        await pressing.Should().ThrowAsync<BuiltinToolException>().WithMessage("*could not be let go of*");
        await moving.Should().ThrowAsync<BuiltinToolException>().WithMessage("*could not be let go of*");

        await world.Holds.Lift(World.Space, world.Sandbox, MouseButton.Left, World.Nothing, World.Token);

        await world.Holds.Guard(World.Space, world.Sandbox, World.Nothing);
        await world.Holds.Press(world.Press(World.Owner(2)), staysDown: true, World.Nothing, World.Token);
    }

    [Fact]
    public async Task A_stuck_screen_is_reset_when_its_sandbox_is_forgotten_after_it_was_destroyed()
    {
        var world = new World { Outcome = ReleaseOutcome.Unreachable };
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            world.Clock.Advance(SandboxPointerHolds.Limit);
        }

        world.Holds.Forget(World.Space, world.Sandbox);

        await world.Holds.Guard(World.Space, world.Sandbox, World.Nothing);
    }

    [Fact]
    public async Task Letting_go_of_everything_says_whether_anything_is_still_held()
    {
        var world = new World { Outcome = ReleaseOutcome.Unreachable };
        await world.Holds.Press(world.Press(), staysDown: true, World.Nothing, World.Token);

        var failed = await world.Holds.ReleaseAll(World.Space, world.Sandbox, World.Token);
        world.Outcome = ReleaseOutcome.Released;
        var done = await world.Holds.ReleaseAll(World.Space, world.Sandbox, World.Token);

        failed.Should().BeFalse();
        done.Should().BeTrue();
        world.Calls.Should().HaveCount(2);
    }

    #endregion

    #region Nested Types

    private sealed class World
    {
        #region Private Fields

        private static readonly PointerOwner defaultOwner = Owner(0);

        private readonly object sync = new();

        private readonly List<PointerPress> calls = [];

        private TaskCompletionSource<ReleaseOutcome>? blocked;

        private TaskCompletionSource? blockedStarted;

        #endregion

        #region Constructors

        public World() => Holds = new SandboxPointerHolds(Clock, new Watcher(this), Release);

        #endregion

        #region Properties

        public static CancellationToken Token => TestContext.Current.CancellationToken;

        public static Guid Space { get; } = Guid.CreateVersion7();

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        public SandboxPointerHolds Holds { get; }

        public Guid Sandbox { get; } = Guid.CreateVersion7();

        public ReleaseOutcome Outcome { get; set; } = ReleaseOutcome.Released;

        public Func<PointerPress, CancellationToken, Task<ReleaseOutcome>>? Behaviour { get; set; }

        /// <summary>Told each message the holds log, as it is logged, on the thread that logs it.</summary>
        public Action<string>? Watching { get; set; }

        public IReadOnlyList<PointerPress> Calls
        {
            get
            {
                lock (sync)
                {
                    return [.. calls];
                }
            }
        }

        #endregion

        #region Public Methods

        public static Task Nothing() => Task.CompletedTask;

        /// <summary>The same session every time for the same number, and a different one for another.</summary>
        public static PointerOwner Owner(int person) => new(Identity(person), Identity(person + 1000));

        /// <summary>Time for work on other threads that a test expects to do nothing.</summary>
        public static Task Idle() => Task.Delay(TimeSpan.FromMilliseconds(100), Token);

        public PointerPress Press(PointerOwner? owner = null, MouseButton button = MouseButton.Left,
            Guid? bot = null) =>
            new(Space, Sandbox, bot ?? Guid.CreateVersion7(), owner ?? defaultOwner, button);

        /// <summary>Makes the next release wait until the test finishes it, and says when it began.</summary>
        public BlockedRelease BlockNextRelease()
        {
            var finish = new TaskCompletionSource<ReleaseOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            blocked = finish;
            blockedStarted = started;
            return new BlockedRelease(started.Task, outcome => finish.TrySetResult(outcome));
        }

        #endregion

        #region Private Methods

        /// <summary>A release that waits for its token, as one stuck on a provider does, then ends in
        /// failure.</summary>
        public static async Task<ReleaseOutcome> Wait(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ReleaseOutcome.Unreachable;
        }

        private static Guid Identity(int number) => new(number, 0, 0, new byte[8]);

        private Task<ReleaseOutcome> Release(PointerPress held, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                calls.Add(held);

                if (blocked is { } waiting)
                {
                    blocked = null;
                    blockedStarted!.TrySetResult();
                    return waiting.Task.WaitAsync(cancellationToken);
                }
            }

            return Behaviour is { } scripted ? scripted(held, cancellationToken) : Task.FromResult(Outcome);
        }

        #endregion
    }

    private sealed class Watcher(World world) : ILogger
    {
        #region Public Methods

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            world.Watching?.Invoke(formatter(state, exception));

        #endregion
    }

    private sealed record BlockedRelease(Task Started, Action<ReleaseOutcome> Finish);

    #endregion
}
