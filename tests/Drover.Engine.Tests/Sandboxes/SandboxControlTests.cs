namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// A person's controls for a bot's sandbox, through <see cref="SandboxControlService"/>, over PostgreSQL and the real
/// broker, against a provider in memory: the state in words, the power controls, the screen, the mouse and keyboard,
/// the terminal, who may use them, the limits that bind them, and what the audit log keeps.
/// </summary>
public sealed class SandboxControlTests(PostgresServer postgres)
{
    #region Private Fields

    private static readonly byte[] png = TestPng.Make(1024, 768);

    #endregion

    #region State

    [Fact]
    public async Task A_bot_with_a_sandbox_policy_and_no_sandbox_yet_reads_as_none_and_looking_makes_nothing()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);

        var status = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        status.Value.Should().Be(new BotSandboxStatus(Available: true, HasSandbox: true, "none", null, Desktop: false, BotWorking: false, DesktopOffered: true));
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task A_bot_whose_last_start_was_given_up_before_the_provider_made_it_says_so_until_it_has_a_sandbox_again()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        await world.Read(async db =>
        {
            // As the sweep leaves a row whose creation never finished: destroyed, with no provider id.
            var row = await db.Sandboxes.SingleAsync(RunWorld.Token);
            row.State = SandboxState.Destroyed;
            row.ProviderSandboxId = string.Empty;
            return true;
        });

        var failed = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        var running = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        failed.Value!.State.Should().Be("none");
        failed.Value.StartFailed.Should().BeTrue("the bot tried to get a sandbox, so no sandbox yet is not the whole story");
        running.Value!.State.Should().Be("running");
        running.Value.StartFailed.Should().BeFalse();
    }

    [Fact]
    public async Task A_bot_without_a_sandbox_policy_says_so()
    {
        await using var world = await ControlWorld(new FakeSandboxProvider(), SandboxPolicy.None);

        var status = await Control(world).GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        var command = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "ls", RunWorld.Token);

        status.Value!.HasSandbox.Should().BeFalse();
        command.Refusal.Should().Be(SandboxControlRefusal.Refused);
        command.Problem.Should().Contain("no sandbox");
    }

    [Fact]
    public async Task A_server_without_a_provider_reports_it_and_refuses_every_action_in_words()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Done.")]));
        await GiveSandbox(world, SandboxPolicy.Private, desktop: false);
        var control = Control(world);

        var status = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        var command = await control.Run(world.SpaceId, world.UserId, world.BotId, "ls", RunWorld.Token);
        var pause = await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        status.Value!.Available.Should().BeFalse();
        command.Refusal.Should().Be(SandboxControlRefusal.Refused);
        command.Problem.Should().Contain("No sandbox provider");
        pause.Refusal.Should().Be(SandboxControlRefusal.Refused);
        (await Actions(world)).Should().BeEmpty("nothing reached the broker");
    }

    [Fact]
    public async Task The_state_reads_running_paused_by_the_provider_after_the_idle_time_and_replacing()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);

        var running = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await world.Read(async db =>
        {
            var row = await db.Sandboxes.SingleAsync(RunWorld.Token);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return true;
        });
        var idle = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await world.Read(async db =>
        {
            var row = await db.Sandboxes.SingleAsync(RunWorld.Token);
            row.State = SandboxState.Retiring;
            row.RetireAfter = DateTimeOffset.UtcNow.AddDays(1);
            return true;
        });
        var replacing = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        running.Value!.State.Should().Be("running");
        running.Value.LastUsedAt.Should().NotBeNull();
        idle.Value!.State.Should().Be("paused", "the provider pauses a sandbox on its own when its idle time runs out");
        replacing.Value!.State.Should().Be("replacing");
    }

    [Fact]
    public async Task The_state_says_when_a_run_of_the_bot_is_working()
    {
        await using var world = await ControlWorld(new FakeSandboxProvider());
        var threadId = await world.NewThread();
        await world.Read(db =>
        {
            var run = new Run
            {
                ThreadId = threadId,
                BotId = world.BotId,
                PrincipalUserId = world.UserId,
                Status = RunStatus.Running,
                LeaseOwner = "another-worker",
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            };
            run.RootRunId = run.Id;
            db.Runs.Add(run);
            return Task.FromResult(true);
        });

        var status = await Control(world).GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        status.Value!.BotWorking.Should().BeTrue();
    }

    #endregion

    #region Power

    [Fact]
    public async Task Pause_pauses_at_the_provider_and_the_next_command_resumes_it_with_its_files()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        provider.Only().Files["/home/user/kept.txt"] = [1, 2, 3];

        var paused = await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        paused.Value!.State.Should().Be("paused");
        provider.Only().State.Should().Be(ProviderSandboxState.Paused);
        (await world.Read(db => db.Sandboxes.SingleAsync(RunWorld.Token))).State.Should().Be(SandboxState.Paused);

        var ran = await control.Run(world.SpaceId, world.UserId, world.BotId, "ls", RunWorld.Token);

        ran.Value!.ExitStatus.Should().Be(0);
        provider.Only().State.Should().Be(ProviderSandboxState.Running);
        provider.Only().Files.Should().ContainKey("/home/user/kept.txt");
        provider.Created.Should().Be(1);
        (await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!.State.Should().Be("running");
    }

    [Fact]
    public async Task Resume_resumes_a_paused_sandbox_and_makes_none_when_there_is_none()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);

        var nothing = await control.Resume(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        nothing.Value!.State.Should().Be("none");
        provider.Created.Should().Be(0);

        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        var resumed = await control.Resume(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        resumed.Value!.State.Should().Be("running");
        provider.Only().State.Should().Be(ProviderSandboxState.Running);
        (await Actions(world)).Should().Equal("sandbox.command", "sandbox.paused", "sandbox.resumed");
    }

    [Fact]
    public async Task Rebuild_destroys_the_old_sandbox_at_the_provider_forgets_its_token_and_makes_a_fresh_one()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        var old = provider.Only();
        old.Files["/home/user/old.txt"] = [1];

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        rebuilt.Value!.State.Should().Be("running");
        old.State.Should().Be(ProviderSandboxState.Gone);
        provider.Created.Should().Be(2);
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().OrderBy(sandbox => sandbox.Id).ToListAsync(RunWorld.Token));
        rows.Select(sandbox => sandbox.State).Should().Equal(SandboxState.Destroyed, SandboxState.Running);
        rows[0].AccessSecretId.Should().BeNull("the old sandbox's token is deleted");
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.SandboxToken, RunWorld.Token))).Should().Be(1);
        (await Actions(world)).Should().Contain("sandbox.rebuilt");
    }

    [Fact]
    public async Task Turn_off_destroys_the_sandbox_and_the_next_use_makes_a_new_one()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        var first = provider.Only();

        var off = await control.TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        off.Value!.State.Should().Be("none");
        first.State.Should().Be(ProviderSandboxState.Gone);
        (await world.Read(db => db.Sandboxes.SingleAsync(RunWorld.Token))).State.Should().Be(SandboxState.Destroyed);
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.SandboxToken, RunWorld.Token))).Should().Be(0);

        var again = await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);

        again.Value!.ExitStatus.Should().Be(0);
        provider.Created.Should().Be(2);
        (await Actions(world)).Should().Equal("sandbox.command", "sandbox.turned_off", "sandbox.command");
    }

    [Fact]
    public async Task Pausing_or_turning_off_when_there_is_no_sandbox_changes_nothing_and_writes_no_audit_row()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);

        (await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!.State.Should().Be("none");
        (await control.TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!.State.Should().Be("none");

        provider.Created.Should().Be(0);
        (await Actions(world)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_pause_that_finishes_after_the_sandbox_was_retired_leaves_it_retiring()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        var due = DateTimeOffset.UtcNow.AddDays(3);
        // PostgreSQL keeps microseconds, not the full tick precision of DateTimeOffset.
        var retirement = new DateTimeOffset(due.Ticks - due.Ticks % 10, TimeSpan.Zero);

        var paused = await PauseAfterRetiring(provider, world, () =>
            world.Transact(services => services.GetRequiredService<SandboxRepository>().RetireLiveForOwner(world.BotId, retirement, RunWorld.Token)));

        var row = await world.Read(db => db.Sandboxes.SingleAsync(RunWorld.Token));

        paused.Refusal.Should().Be(SandboxControlRefusal.None);
        paused.Value!.State.Should().Be("replacing");
        row.State.Should().Be(SandboxState.Retiring);
        row.RetireAfter.Should().Be(retirement);
        (await Actions(world)).Should().Equal("sandbox.command");
    }

    [Fact]
    public async Task A_pause_that_finishes_after_an_audience_narrowed_does_not_bring_the_sandbox_back()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        var due = DateTimeOffset.UtcNow.AddDays(3);
        // PostgreSQL keeps microseconds, not the full tick precision of DateTimeOffset.
        var retirement = new DateTimeOffset(due.Ticks - due.Ticks % 10, TimeSpan.Zero);

        var paused = await PauseAfterRetiring(provider, world, () =>
            world.Transact(services => services.GetRequiredService<SandboxRepository>().RetireLive(retirement, RunWorld.Token)));

        var row = await world.Read(db => db.Sandboxes.SingleAsync(RunWorld.Token));

        paused.Value!.State.Should().Be("replacing");
        row.State.Should().Be(SandboxState.Retiring);
        (await Actions(world)).Should().Equal("sandbox.command");
    }

    [Fact]
    public async Task A_pause_that_finishes_after_turn_off_leaves_the_sandbox_destroyed_and_writes_no_audit_row()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);

        var paused = await PauseAfterRetiring(provider, world, () => world.Read(async db =>
        {
            (await db.Sandboxes.SingleAsync(RunWorld.Token)).State = SandboxState.Destroyed;
            return true;
        }));

        var row = await world.Read(db => db.Sandboxes.SingleAsync(RunWorld.Token));

        paused.Refusal.Should().Be(SandboxControlRefusal.None);
        paused.Value!.State.Should().Be("none");
        row.State.Should().Be(SandboxState.Destroyed);
        (await Actions(world)).Should().Equal("sandbox.command");
    }

    [Fact]
    public async Task A_pause_of_a_retired_sandbox_whose_bot_already_has_a_new_one_does_not_fail()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);

        var paused = await PauseAfterRetiring(provider, world, () => world.Read(async db =>
        {
            var older = await db.Sandboxes.SingleAsync(RunWorld.Token);
            older.State = SandboxState.Retiring;
            older.RetireAfter = DateTimeOffset.UtcNow;
            db.Sandboxes.Add(new Sandbox
            {
                SpaceId = world.SpaceId,
                OwnerBotId = world.BotId,
                Provider = "fake",
                ProviderSandboxId = "replacement",
                State = SandboxState.Creating,
            });
            return true;
        }));

        paused.Refusal.Should().Be(SandboxControlRefusal.None);
        paused.Value!.State.Should().Be("starting");
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().ToListAsync(RunWorld.Token));
        rows.Should().ContainSingle(row => row.ProviderSandboxId == "replacement" && row.State == SandboxState.Creating);
        rows.Should().ContainSingle(row => row.ProviderSandboxId != "replacement" && row.State == SandboxState.Retiring);
    }

    #endregion

    #region Screen And Input

    [Fact]
    public async Task The_screen_starts_the_display_takes_a_screenshot_and_removes_its_file_as_a_bots_does()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);

        var shot = await Control(world).Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        shot.Value!.Png.Should().Equal(png);
        provider.Commands.Select(command => command.Program).Should().Equal("/bin/bash", "scrot", "rm");
        provider.Only().Files.Should().BeEmpty("the screenshot file is removed");
        (await Actions(world)).Should().BeEmpty("looking is not an action the audit log keeps");
    }

    [Fact]
    public async Task Screens_asked_for_while_one_is_being_taken_share_it()
    {
        const int Requests = 5;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = DesktopProvider(async cancellationToken => await release.Task.WaitAsync(cancellationToken));
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        var joined = 0;
        var allJoined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.ScreenRequested = () =>
        {
            if (Interlocked.Increment(ref joined) == Requests)
            {
                allJoined.TrySetResult();
            }
        };

        // All five go through the public entry point, which builds the key and the caller itself. The provider holds
        // the one screenshot until every request has joined it, so none can arrive after it is done and start another.
        var requests = Enumerable.Range(0, Requests)
            .Select(_ => control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token))
            .ToArray();
        try
        {
            await allJoined.Task.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        }
        finally
        {
            release.TrySetResult();
        }

        var shots = await Task.WhenAll(requests);

        shots.Should().OnlyContain(shot => shot.Refusal == SandboxControlRefusal.None && shot.Value!.Png.SequenceEqual(png));
        provider.Commands.Count(command => command.Program == "scrot").Should().Be(1, "five requests that met share one screenshot");
    }

    [Fact]
    public async Task A_screen_asked_for_after_the_shared_one_is_done_takes_a_new_one()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);

        (await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        (await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);

        provider.Commands.Count(command => command.Program == "scrot").Should().Be(2, "nothing is kept to share once a screenshot is done");
    }

    [Fact]
    public async Task A_refresh_finishing_after_pause_leaves_the_sandbox_paused()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        (await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.BeforeRead = (_, cancellation) =>
        {
            reading.TrySetResult();
            return release.Task.WaitAsync(cancellation);
        };

        var refresh = control.Screen(world.SpaceId, world.UserId, world.BotId, wake: false, RunWorld.Token);
        try
        {
            await reading.Task.WaitAsync(RunWorld.Token);
            (await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!.State.Should().Be("paused");
        }
        finally
        {
            release.TrySetResult();
        }

        await refresh;
        (await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!.State.Should().Be("paused");
        (await control.Screen(world.SpaceId, world.UserId, world.BotId, wake: false, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.Refused);
        provider.Only().State.Should().Be(ProviderSandboxState.Paused);
    }

    [Fact]
    public async Task A_sandbox_without_a_screen_refuses_the_screen_in_words()
    {
        await using var world = await ControlWorld(DesktopProvider(), desktop: false);

        var shot = await Control(world).Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        shot.Refusal.Should().Be(SandboxControlRefusal.Refused);
        shot.Problem.Should().Contain("no screen");
    }

    [Fact]
    public async Task A_deployment_without_desktop_sandboxes_refuses_a_persons_screen_too()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true, policy: DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = false });

        var shot = await Control(world).Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        shot.Refusal.Should().Be(SandboxControlRefusal.Refused);
        shot.Problem.Should().Contain(nameof(DeploymentPolicy.AllowDesktopSandboxes));
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task The_state_says_whether_the_server_offers_screens_so_a_sandbox_without_one_can_say_why()
    {
        await using var offered = await ControlWorld(DesktopProvider(), desktop: false);
        await using var forbidden = await ControlWorld(DesktopProvider(), desktop: true, policy: DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = false });

        var off = (await Control(offered).GetStatus(offered.SpaceId, offered.UserId, offered.BotId, RunWorld.Token)).Value!;
        var refused = (await Control(forbidden).GetStatus(forbidden.SpaceId, forbidden.UserId, forbidden.BotId, RunWorld.Token)).Value!;

        off.Should().Match<BotSandboxStatus>(status => !status.Desktop && status.DesktopOffered, "Desktop is off, and turning it on would give it a screen");
        refused.Should().Match<BotSandboxStatus>(status => !status.Desktop && !status.DesktopOffered, "the server offers none, whatever the bot's setting");
    }

    [Fact]
    public async Task A_bot_without_a_sandbox_still_hears_whether_the_server_offers_screens()
    {
        await using var world = await ControlWorld(new FakeSandboxProvider(), SandboxPolicy.None, policy: DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = false });

        var status = (await Control(world).GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!;

        status.Should().Match<BotSandboxStatus>(found => !found.HasSandbox && !found.DesktopOffered);
    }

    [Fact]
    public async Task A_persons_click_text_key_and_scroll_are_the_same_commands_a_bot_sends()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        provider.Commands.Clear();

        SandboxInputRequest[] inputs =
        [
            new("click", X: 512, Y: 300, Button: "right", Twice: true),
            new("type", Text: "hello -- $(whoami)"),
            new("key", Keys: ["ctrl", "l"]),
            new("scroll", Direction: "down", Amount: 2),
        ];

        foreach (var input in inputs)
        {
            (await control.Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        }

        var actions = provider.Commands.Where(command => command.Program == "xdotool").Select(command => command.Arguments).ToList();
        actions.Should().HaveCount(4);
        actions[0].Should().Equal(DesktopCommands.Click(512, 300, MouseButton.Right, twice: true).Arguments);
        actions[1].Should().Equal(DesktopCommands.Type("hello -- $(whoami)").Arguments);
        actions[2].Should().Equal(DesktopCommands.Key(["ctrl", "l"]).Arguments);
        actions[3].Should().Equal(DesktopCommands.Scroll(ScrollDirection.Down, 2, null, null).Arguments);
    }

    [Theory]
    [InlineData("key", null, null, null, "windows")]
    [InlineData("click", 1024, 10, null, null)]
    [InlineData("type", null, null, "", null)]
    [InlineData("wave", null, null, null, null)]
    public async Task A_wrong_input_is_refused_before_the_sandbox_is_touched(string kind, int? x, int? y, string? text, string? key)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);

        var input = new SandboxInputRequest(kind, X: x, Y: y, Text: text, Keys: key is null ? null : [key]);
        var result = await Control(world).Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
        result.Problem.Should().NotBeNullOrWhiteSpace();
        provider.Commands.Should().BeEmpty();
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task Typed_text_is_capped_at_the_bots_own_length()
    {
        await using var world = await ControlWorld(DesktopProvider(), desktop: true);

        var input = new SandboxInputRequest("type", Text: new string('a', DesktopCommands.MaxTypedCharacters + 1));
        var result = await Control(world).Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
    }

    [Fact]
    public async Task Input_makes_no_sandbox_when_there_is_none()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);

        var result = await Control(world).Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("key", Keys: ["enter"]), RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        result.Problem.Should().Contain("no sandbox yet");
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task The_audit_log_keeps_the_kind_of_input_and_never_the_text_or_the_command()
    {
        await using var world = await ControlWorld(DesktopProvider(), desktop: true);
        var control = Control(world);
        var typed = $"typed-{Guid.NewGuid():N}";
        var password = $"sample-{Guid.NewGuid():N}";
        var command = $"echo command-{Guid.NewGuid():N}";
        await control.Run(world.SpaceId, world.UserId, world.BotId, command, RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("type", Text: typed), RunWorld.Token);
        var sent = await control.Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("password", Text: password), RunWorld.Token);
        sent.Refusal.Should().Be(SandboxControlRefusal.None);

        var entries = await world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action.StartsWith("sandbox.")).OrderBy(entry => entry.Id).ToListAsync(RunWorld.Token));

        entries.Select(entry => entry.Action).Should().Equal("sandbox.command", "sandbox.input", "sandbox.input");
        entries.Should().OnlyContain(entry => entry.ActorId == world.UserId && entry.TargetKind == "bot" && entry.TargetId == world.BotId);
        entries[1].Data.GetProperty("input").GetString().Should().Be("type");
        entries[2].Data.GetProperty("input").GetString().Should().Be("password");
        entries.Should().OnlyContain(entry => entry.Data.GetProperty("outcome").GetString() == "done");
        entries.Should().OnlyContain(entry => !entry.Data.GetRawText().Contains(typed)
            && !entry.Data.GetRawText().Contains(password) && !entry.Data.GetRawText().Contains("command-"));
    }

    #endregion

    #region Pointer

    [Fact]
    public async Task A_persons_press_move_and_release_are_the_commands_a_bot_sends()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        SandboxInputRequest[] inputs =
        [
            new("down", X: 100, Y: 200, Button: "right"),
            new("move", X: 300, Y: 400),
            new("up", X: 310, Y: 410, Button: "right"),
            new("up"),
        ];

        foreach (var input in inputs)
        {
            (await control.Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token)).Refusal
                .Should().Be(SandboxControlRefusal.None);
        }

        Pointer(provider).Should().HaveCount(4);
        Pointer(provider)[0].Should().Equal(DesktopCommands.MouseDown(100, 200, MouseButton.Right).Arguments);
        Pointer(provider)[1].Should().Equal(DesktopCommands.MouseMove(300, 400).Arguments);
        Pointer(provider)[2].Should().Equal(DesktopCommands.MouseUp(MouseButton.Right, 310, 410).Arguments);
        Pointer(provider)[3].Should().Equal(DesktopCommands.MouseUp(MouseButton.Left, null, null).Arguments,
            "no button named is the left one");
    }

    [Fact]
    public async Task A_drag_is_one_command_that_presses_moves_and_releases_and_leaves_nothing_held()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        var result = await control.Input(world.SpaceId, world.UserId, world.BotId, new("drag", X: 10, Y: 20, X2: 600,
            Y2: 500), RunWorld.Token);
        await Advance(clock, control, SandboxPointerHolds.Limit * 2);

        result.Refusal.Should().Be(SandboxControlRefusal.None);
        Pointer(provider).Should().ContainSingle().Which.Should().Equal(DesktopCommands.Drag(10, 20, 600, 500,
            MouseButton.Left).Arguments);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("provider")]
    [InlineData("unexpected")]
    public async Task A_press_that_fails_is_let_go_of_before_the_request_answers(string failure)
    {
        var provider = PointerProvider((command, _) => IsPress(command) ? Failing(failure) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var press = () => control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);

        if (failure is "unexpected")
        {
            await press.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            (await press()).Refusal.Should().Be(SandboxControlRefusal.Refused);
        }

        Pointer(provider).Select(arguments => arguments.Contains("mousedown")).Should().Equal(true, false);
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_press_whose_request_is_cancelled_is_let_go_of()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) => IsPress(command) ? Holding(running, never, token) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        using var caller = new CancellationTokenSource();

        var press = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6, Button: "middle"),
            caller.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        await caller.CancelAsync();
        var cancelled = async () => await press;

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        Pointer(provider).Select(arguments => arguments.Contains("mousedown")).Should().Equal(true, false);
        Pointer(provider)[1].Should().Equal("mouseup", "2");
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("unexpected")]
    public async Task A_drag_that_fails_lets_go_of_its_button(string failure)
    {
        var provider = PointerProvider((command, _) => IsDrag(command) ? Failing(failure) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var drag = () => control.Input(world.SpaceId, world.UserId, world.BotId, new("drag", X: 5, Y: 6, X2: 50,
            Y2: 60), RunWorld.Token);

        if (failure is "unexpected")
        {
            await drag.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            (await drag()).Refusal.Should().Be(SandboxControlRefusal.Refused);
        }

        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_drag_whose_request_is_cancelled_lets_go_of_its_button()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) => IsDrag(command) ? Holding(running, never, token) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        using var caller = new CancellationTokenSource();

        var drag = control.Input(world.SpaceId, world.UserId, world.BotId, new("drag", X: 5, Y: 6, X2: 50, Y2: 60,
            Button: "right"), caller.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        await caller.CancelAsync();
        var cancelled = async () => await drag;

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "3");
    }

    [Fact]
    public async Task A_button_with_no_move_or_release_is_let_go_of_after_ten_seconds()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        await Advance(clock, control, SandboxPointerHolds.Limit - TimeSpan.FromSeconds(1));
        Pointer(provider).Should().ContainSingle("the button is still held within its time");

        await Advance(clock, control, TimeSpan.FromSeconds(1));

        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_move_keeps_a_held_button_for_another_ten_seconds()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        await Advance(clock, control, TimeSpan.FromSeconds(9));
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("move", X: 50, Y: 60), RunWorld.Token);
        await Advance(clock, control, TimeSpan.FromSeconds(9));
        Pointer(provider).Should().HaveCount(2, "the press and the move, with nothing let go of yet");

        await Advance(clock, control, TimeSpan.FromSeconds(1));

        Pointer(provider)[2].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_release_ends_the_wait_so_the_time_limit_has_nothing_to_let_go_of()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("up"), RunWorld.Token);

        await Advance(clock, control, SandboxPointerHolds.Limit * 3);

        Pointer(provider).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("rebuild")]
    [InlineData("turn off")]
    public async Task A_held_button_is_let_go_of_before_the_sandbox_is_paused_rebuilt_or_turned_off(string power)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        var letGoBeforeThePower = false;
        provider.BeforePause = (_, _) =>
        {
            letGoBeforeThePower = Pointer(provider).Any(arguments => arguments.Contains("mouseup"));
            return Task.CompletedTask;
        };

        var done = power switch
        {
            "pause" => await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
            "rebuild" => await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
            _ => await control.TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
        };

        done.Refusal.Should().Be(SandboxControlRefusal.None);
        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "1");

        if (power is "pause")
        {
            letGoBeforeThePower.Should().BeTrue("a paused sandbox keeps the button down when it comes back");
        }
    }

    [Fact]
    public async Task A_held_button_is_let_go_of_when_the_server_stops()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6, Button: "middle"),
            RunWorld.Token);

        await control.DisposeAsync();

        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "2");
    }

    [Fact]
    public async Task A_release_that_fails_is_tried_again_after_the_same_time()
    {
        var failures = 1;
        var provider = PointerProvider((command, _) => IsRelease(command)
            && Interlocked.Decrement(ref failures) >= 0 ? Failing("provider") : null);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        await Advance(clock, control, SandboxPointerHolds.Limit);
        Pointer(provider).Should().HaveCount(2, "the first release failed");
        await Advance(clock, control, SandboxPointerHolds.Limit);
        Pointer(provider).Should().HaveCount(3, "the second try worked");
        await Advance(clock, control, SandboxPointerHolds.Limit * 3);

        Pointer(provider).Should().HaveCount(3, "the second release worked, so nothing is held any more");
    }

    [Theory]
    [InlineData("down", -1, 5, 5, 5)]
    [InlineData("down", 1024, 5, 5, 5)]
    [InlineData("move", 5, 768, 5, 5)]
    [InlineData("up", 5, -1, 5, 5)]
    [InlineData("drag", 5, 5, 1024, 5)]
    [InlineData("drag", 5, 5, 5, -2)]
    [InlineData("drag", 1100, 5, 5, 5)]
    public async Task A_pointer_point_off_the_screen_is_refused_in_a_plain_sentence_before_the_sandbox_is_touched(
        string kind,
        int x,
        int y,
        int x2,
        int y2)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        var result = await control.Input(world.SpaceId, world.UserId, world.BotId, new(kind, X: x, Y: y, X2: x2,
            Y2: y2), RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
        result.Problem.Should().Contain("off the screen");
        Pointer(provider).Should().BeEmpty();
        (await Actions(world)).Should().BeEmpty("a refusal before the sandbox is reached is not an attempt on it");
    }

    [Theory]
    [InlineData("down", null, null, null, null, null)]
    [InlineData("down", 5, 5, "wheel", null, null)]
    [InlineData("move", 5, null, null, null, null)]
    [InlineData("up", 5, null, null, null, null)]
    [InlineData("drag", 5, 5, null, null, null)]
    [InlineData("drag", 5, 5, null, 10, null)]
    [InlineData("drag", 5, 5, "left", null, 10)]
    public async Task A_wrong_pointer_request_is_refused_before_the_sandbox_is_touched(string kind, int? x, int? y,
        string? button, int? x2, int? y2)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        var result = await control.Input(world.SpaceId, world.UserId, world.BotId, new(kind, X: x, Y: y, Button: button,
            X2: x2, Y2: y2), RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
        result.Problem.Should().NotBeNullOrWhiteSpace();
        Pointer(provider).Should().BeEmpty();
    }

    [Fact]
    public async Task A_person_who_may_not_change_the_bot_cannot_press_and_nothing_reaches_the_sandbox()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var member = await world.AddMember(MembershipRole.Member);

        List<SandboxControlResult<bool>> results =
        [
            await control.Input(world.SpaceId, member, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token),
            await control.Input(world.SpaceId, member, world.BotId, new("drag", X: 5, Y: 6, X2: 7, Y2: 8),
                RunWorld.Token),
            await control.Input(world.SpaceId, Guid.CreateVersion7(), world.BotId, new("down", X: 5, Y: 6),
                RunWorld.Token),
        ];

        results.Select(result => result.Refusal).Should().Equal(SandboxControlRefusal.NotPermitted,
            SandboxControlRefusal.NotPermitted, SandboxControlRefusal.NotFound);
        Pointer(provider).Should().BeEmpty("not even a release was sent: nothing was pressed");
    }

    [Fact]
    public async Task A_press_on_a_sandbox_whose_screen_the_server_does_not_offer_is_refused_as_a_click_is()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true,
            policy: DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = false });
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        provider.Commands.Clear();

        var click = await Control(world).Input(world.SpaceId, world.UserId, world.BotId, new("click", X: 5, Y: 6),
            RunWorld.Token);
        var press = await Control(world).Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);

        press.Refusal.Should().Be(click.Refusal).And.Be(SandboxControlRefusal.Refused);
        press.Problem.Should().Be(click.Problem);
        provider.Commands
            .Should().BeEmpty("neither the press nor a release reached a screen the server does not offer");
    }

    [Fact]
    public async Task A_pointer_input_writes_one_audit_row_with_its_kind_and_never_a_position()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        SandboxInputRequest[] inputs =
        [
            new("down", X: 417, Y: 293),
            new("move", X: 418, Y: 294),
            new("up", X: 419, Y: 295),
            new("drag", X: 417, Y: 293, X2: 1017, Y2: 701),
        ];

        foreach (var input in inputs)
        {
            await control.Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token);
        }

        var entries = await world.Read(db => db.AuditLog.AsNoTracking()
            .Where(entry => entry.Action.StartsWith("sandbox."))
            .OrderBy(entry => entry.Id)
            .ToListAsync(RunWorld.Token));

        entries.Select(entry => entry.Data.GetProperty("input").GetString()).Should().Equal("down", "move", "up",
            "drag");
        entries.Select(entry => entry.Data.GetProperty("outcome").GetString()).Should().Equal("done", "done", "done",
            "done");
        entries.Should().OnlyContain(entry => entry.Action == "sandbox.input" && entry.ActorId == world.UserId
            && entry.TargetId == world.BotId);
        entries.Should().OnlyContain(entry => !entry.Data.GetRawText().Contains("417")
            && !entry.Data.GetRawText().Contains("293") && !entry.Data.GetRawText().Contains("701"));
    }

    [Fact]
    public async Task A_press_that_fails_and_is_let_go_of_still_writes_only_the_one_row_for_the_attempt()
    {
        var provider = PointerProvider((command, _) => IsPress(command) ? Failing("exit") : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        (await Actions(world)).Should().Equal("sandbox.input");
        (await Outcomes(world)).Should().Equal("refused");
    }

    [Fact]
    public async Task A_pointer_input_is_logged_without_its_position_and_without_the_command()
    {
        var provider = PointerProvider((command, _) => IsPress(command) ? Failing("exit") : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);

        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 1003, Y: 703), RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 1999, Y: 1777), RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("drag", X: 1003, Y: 703, X2: 1004, Y2: 704),
            RunWorld.Token);

        var log = world.Hosts[^1].Log.Tail();
        log.Should().NotContain("xdotool").And.NotContain("mousedown").And.NotContain("off the screen");
        System.Text.RegularExpressions.Regex.IsMatch(log,
            "(?<![0-9a-fA-F-])(1003|703|1999|1777|1004|704)(?![0-9a-fA-F-])").Should().BeFalse();
    }

    [Fact]
    public async Task A_move_from_another_session_does_not_keep_a_button_that_a_session_pressed()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var pressing = Guid.CreateVersion7();
        var another = Guid.CreateVersion7();
        var press = new SandboxInputRequest("down", X: 5, Y: 6);
        await control.Input(world.SpaceId, world.UserId, world.BotId, press, RunWorld.Token, pressing);

        await Advance(clock, control, TimeSpan.FromSeconds(9));
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("move", X: 50, Y: 60), RunWorld.Token,
            another);
        await Advance(clock, control, TimeSpan.FromSeconds(1));

        Pointer(provider).Should().HaveCount(3);
        Pointer(provider)[^1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_button_one_session_holds_is_refused_to_another_and_let_go_of_by_anyone()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var press = new SandboxInputRequest("down", X: 5, Y: 6);
        await control.Input(world.SpaceId, world.UserId, world.BotId, press, RunWorld.Token, Guid.CreateVersion7());

        var other = await control.Input(world.SpaceId, world.UserId, world.BotId, press, RunWorld.Token,
            Guid.CreateVersion7());
        var lifted = await control.Input(world.SpaceId, world.UserId, world.BotId, new("up"), RunWorld.Token,
            Guid.CreateVersion7());
        var again = await control.Input(world.SpaceId, world.UserId, world.BotId, press, RunWorld.Token,
            Guid.CreateVersion7());

        other.Refusal.Should().Be(SandboxControlRefusal.Refused);
        other.Problem.Should().Contain("Another person");
        lifted.Refusal.Should().Be(SandboxControlRefusal.None);
        again.Refusal.Should().Be(SandboxControlRefusal.None);
    }

    [Fact]
    public async Task A_sandbox_that_cannot_be_reached_keeps_the_button_pending_until_it_can_be()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await SetSandboxRow(world, row => row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1));
        provider.PauseAll();

        await AdvanceTimes(clock, control, 3);
        Pointer(provider)
            .Should().ContainSingle("a sandbox the provider paused cannot be reached, which is not a release");
        await SetSandboxRow(world, row => row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10));
        provider.Only().State = ProviderSandboxState.Running;
        await Advance(clock, control, SandboxPointerHolds.Limit);

        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_sandbox_that_was_destroyed_ends_the_hold_for_good()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await SetSandboxRow(world, row => row.State = SandboxState.Destroyed);

        await AdvanceTimes(clock, control, 1);
        await SetSandboxRow(world, row => row.State = SandboxState.Running);
        await AdvanceTimes(clock, control, 3);

        Pointer(provider)
            .Should().ContainSingle("the sandbox was confirmed destroyed, so nothing is left to let go of");
    }

    [Fact]
    public async Task A_button_pressed_on_a_sandbox_that_was_replaced_is_never_let_go_of_on_the_new_one()
    {
        var released = new ConcurrentQueue<string>();
        var provider = DesktopProvider();
        var desktop = provider.Behaviour;
        provider.Behaviour = (command, token, cancellationToken) =>
        {
            if (IsRelease(command))
            {
                released.Enqueue(provider.WithToken(token).Id);
            }

            return desktop(command, token, cancellationToken);
        };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        var pressedOn = provider.Only().Id;
        await SetSandboxRow(world, row =>
        {
            row.State = SandboxState.Retiring;
            row.RetireAfter = DateTimeOffset.UtcNow.AddDays(1);
        });
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        provider.Created.Should().Be(2);

        await AdvanceTimes(clock, control, 3);

        released.Should().Equal(
            [pressedOn],
            "the old display is live until it is destroyed, so it is let go of there once, never on the new one");
    }

    [Fact]
    public async Task One_deadline_covers_a_release_so_a_command_that_hangs_is_stopped_and_tried_again()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hung = false;
        var provider = PointerProvider((command, token) =>
            IsRelease(command) && !Interlocked.Exchange(ref hung, true) ? Holding(running, never, token) : null);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        clock.Advance(SandboxPointerHolds.Limit);
        await running.Task.WaitAsync(RunWorld.Token);
        Pointer(provider).Should().HaveCount(2);

        await Advance(clock, control, DesktopCommands.ReleaseLimit);
        Pointer(provider).Should().HaveCount(2, "the deadline ended the release that hung");
        await Advance(clock, control, SandboxPointerHolds.Limit);

        Pointer(provider).Should().HaveCount(3);
        Pointer(provider)[2].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task After_the_last_failed_release_the_screen_takes_no_input_until_a_person_lets_go_of_the_button()
    {
        var letting = false;
        var provider = PointerProvider((command, _) => IsRelease(command) && !letting ? Failing("exit") : null);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Advance(clock, control, SandboxPointerHolds.Limit);
        }

        var click = await control.Input(
            world.SpaceId,
            world.UserId,
            world.BotId,
            new("click", X: 1, Y: 1),
            RunWorld.Token);
        var move = await control.Input(world.SpaceId, world.UserId, world.BotId, new("move", X: 7, Y: 8),
            RunWorld.Token);
        var typed = await control.Input(world.SpaceId, world.UserId, world.BotId, new("key", Keys: ["enter"]),
            RunWorld.Token);
        letting = true;
        var up = await control.Input(world.SpaceId, world.UserId, world.BotId, new("up"), RunWorld.Token);
        var after = await control.Input(world.SpaceId, world.UserId, world.BotId, new("click", X: 1, Y: 1),
            RunWorld.Token);

        click.Problem.Should().Contain("could not be let go of");
        move.Refusal.Should().Be(SandboxControlRefusal.Refused);
        typed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        up.Refusal.Should().Be(SandboxControlRefusal.None);
        after.Refusal.Should().Be(SandboxControlRefusal.None);
        Pointer(provider).Count(arguments => arguments.Contains("mouseup")).Should().Be(7);
    }

    [Fact]
    public async Task A_sandbox_is_not_paused_while_a_button_on_it_cannot_be_let_go_of()
    {
        var provider = PointerProvider((command, _) => IsRelease(command) ? Failing("exit") : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        var pause = await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        pause.Refusal.Should().Be(SandboxControlRefusal.Refused);
        pause.Problem.Should().Contain("not paused");
        provider.Pauses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rebuild_goes_ahead_when_a_button_cannot_be_let_go_of_and_the_new_display_takes_input()
    {
        var provider = PointerProvider((command, _) => IsRelease(command) ? Failing("exit") : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        var click = await control.Input(world.SpaceId, world.UserId, world.BotId, new("click", X: 1, Y: 1),
            RunWorld.Token);

        rebuilt.Refusal.Should().Be(SandboxControlRefusal.None);
        click.Refusal.Should().Be(SandboxControlRefusal.None);
    }

    [Fact]
    public async Task A_press_that_worked_is_let_go_of_when_recording_the_use_is_cancelled_or_fails()
    {
        using var caller = new CancellationTokenSource();
        var provider = PointerProvider((command, _) => IsPress(command) ? Cancelling(caller) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var press = () => control.Input(
            world.SpaceId,
            world.UserId,
            world.BotId,
            new("down", X: 5, Y: 6),
            caller.Token);

        await press.Should().ThrowAsync<OperationCanceledException>();

        Pointer(provider).Select(arguments => arguments.Contains("mousedown")).Should().Equal(true, false);
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_press_that_waits_for_a_pause_is_refused_after_it_and_never_reaches_the_paused_display()
    {
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        provider.BeforePause = async (_, cancellationToken) =>
        {
            pausing.TrySetResult();
            await finish.Task.WaitAsync(cancellationToken);
        };
        control.Holds.Waiting = () => atTheGate.TrySetResult();

        var pause = control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await pausing.Task.WaitAsync(RunWorld.Token);
        var press = control.Input(
            world.SpaceId,
            world.UserId,
            world.BotId,
            new("down", X: 7, Y: 8, Button: "right"),
            RunWorld.Token);
        await atTheGate.Task.WaitAsync(RunWorld.Token);
        Pointer(provider).Select(arguments => arguments.Contains("mousedown")).Should().Equal(
            [true, false],
            "only the first press, and its release, reached the display while the pause was under way");
        finish.SetResult();

        var paused = await pause;
        var pressed = await press;

        paused.Refusal.Should().Be(SandboxControlRefusal.None);
        provider.Pauses.Should().ContainSingle();
        pressed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        pressed.Problem.Should().Contain("paused or replaced");
        Pointer(provider).Select(arguments => arguments.Contains("mousedown")).Should().Equal(
            [true, false],
            "the press that waited sent nothing to the paused display");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
        (await Outcomes(world)).Should().BeEquivalentTo(
            ["done", "done", "refused"],
            "the first press, the pause and the refused press each wrote one row, in the order they ended");
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("turn off")]
    public async Task A_power_control_that_found_no_sandbox_does_nothing_if_one_went_live_before_it_acted(string power)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        control.AfterFirstLookup = async () =>
        {
            control.AfterFirstLookup = null;
            await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        };

        var result = power is "pause"
            ? await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)
            : await control.TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        result.Problem.Should().Contain("changed while this was under way");
        provider.Created.Should().Be(1, "the sandbox went live between the look and the act");
        provider.Pauses.Should().BeEmpty();
        provider.Destroys.Should().BeEmpty();
    }

    [Fact]
    public async Task A_press_waiting_behind_a_pause_that_failed_after_the_provider_paused_is_refused()
    {
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        using var request = new CancellationTokenSource();
        provider.BeforePause = async (_, cancellationToken) =>
        {
            pausing.TrySetResult();
            await finish.Task.WaitAsync(cancellationToken);
            await request.CancelAsync();
        };
        control.Holds.Waiting = () => atTheGate.TrySetResult();

        var pause = control.Pause(world.SpaceId, world.UserId, world.BotId, request.Token);
        await pausing.Task.WaitAsync(RunWorld.Token);
        var press = control.Input(
            world.SpaceId,
            world.UserId,
            world.BotId,
            new("down", X: 7, Y: 8),
            RunWorld.Token);
        await atTheGate.Task.WaitAsync(RunWorld.Token);
        finish.SetResult();
        var paused = async () => await pause;

        await paused.Should().ThrowAsync<OperationCanceledException>("the request ended after the provider had paused");
        var pressed = await press;

        pressed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        pressed.Problem.Should().Contain("paused or replaced");
        Pointer(provider).Should().BeEmpty("nothing was sent to a display that may be paused");
    }

    [Fact]
    public async Task A_press_that_held_a_sandbox_while_a_pause_finished_is_refused_before_it_sends_anything()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) => command.Arguments is [.., "sleep 1"]
            ? HoldCommand(running, release, token)
            : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var command = control.Run(world.SpaceId, world.UserId, world.BotId, "sleep 1", RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        broker.LeaseHeld = () => leaseHeld.TrySetResult();

        var press = control.Input(
            world.SpaceId,
            world.UserId,
            world.BotId,
            new("down", X: 7, Y: 8),
            RunWorld.Token);
        await leaseHeld.Task.WaitAsync(RunWorld.Token);
        var paused = await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        release.SetResult();
        await command;
        var pressed = await press;

        paused.Refusal.Should().Be(SandboxControlRefusal.None);
        pressed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        pressed.Problem.Should().Contain("paused or replaced");
        Pointer(provider).Should().BeEmpty("the press sent nothing to the display the pause had taken");
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("turn off")]
    [InlineData("rebuild")]
    public async Task A_power_control_acts_only_on_the_sandbox_it_was_made_ready_for(string power)
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var other = Guid.CreateVersion7();
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var person = new SandboxCaller(world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId,
            world.UserId), world.BotId);

        var act = power switch
        {
            "pause" => () => broker.Pause(person, RunWorld.Token, other),
            "turn off" => () => broker.TurnOff(person, RunWorld.Token, other),
            _ => (Func<Task>)(async () => await broker.Rebuild(person, RunWorld.Token, other)),
        };

        await act.Should().ThrowAsync<BuiltinToolException>().WithMessage("*changed while this was under way*");
        provider.Pauses.Should().BeEmpty();
        provider.Destroys.Should().BeEmpty();
        provider.Created.Should().Be(1, "nothing was replaced");
        (await broker.CurrentSandbox(person, RunWorld.Token)).Should().NotBeNull("the sandbox is still live");
    }

    #endregion

    #region The Physical Sandbox, The Gate And The Audit Row

    [Theory]
    [InlineData("down", "exit")]
    [InlineData("drag", "exit")]
    [InlineData("down", "cancel")]
    public async Task A_press_that_fails_right_after_waking_a_paused_sandbox_is_let_go_of_on_the_sandbox_it_woke(
        string kind,
        string failure)
    {
        using var caller = new CancellationTokenSource();
        var provider = PointerProvider((command, _) => IsPress(command) || IsDrag(command)
            ? failure is "cancel" ? Cancelling(caller) : Failing("exit")
            : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal
            .Should().Be(SandboxControlRefusal.None);
        SandboxInputRequest input = kind is "drag" ? new("drag", X: 5, Y: 6, X2: 50, Y2: 60) : new("down", X: 5, Y: 6);

        var pressing = () => control.Input(world.SpaceId, world.UserId, world.BotId, input, caller.Token);

        if (failure is "cancel")
        {
            await pressing.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            (await pressing()).Refusal.Should().Be(SandboxControlRefusal.Refused);
        }

        Pointer(provider).Should().HaveCount(2, "the row read as paused, but the sandbox the press woke is let go of");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_button_is_let_go_of_on_its_own_sandbox_after_the_bot_that_pressed_it_is_removed()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.Status = BotStatus.Removed;
            return true;
        });

        await Advance(clock, control, SandboxPointerHolds.Limit);

        Pointer(provider).Should().HaveCount(2, "the release finds the sandbox by its own row, not through the bot");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
    }

    [Fact]
    public async Task A_resume_does_nothing_when_the_bot_has_another_sandbox_by_the_time_it_acts()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        control.AfterFirstLookup = async () =>
        {
            control.AfterFirstLookup = null;
            await SetSandboxRow(world, row =>
            {
                row.State = SandboxState.Retiring;
                row.RetireAfter = DateTimeOffset.UtcNow.AddDays(1);
            });
            await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        };

        var resumed = await control.Resume(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        resumed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        resumed.Problem.Should().Contain("changed while this was under way");
        provider.Created.Should().Be(2);
        Pointer(provider).Should().ContainSingle("the resume let go of nothing on either sandbox");
    }

    [Fact]
    public async Task A_press_that_comes_while_a_resume_is_under_way_is_not_let_go_of_by_it()
    {
        var resuming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal
            .Should().Be(SandboxControlRefusal.None);
        var resumes = 0;
        provider.BeforeResume = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref resumes) is 1)
            {
                resuming.TrySetResult();
                await finish.Task.WaitAsync(cancellationToken);
            }
        };
        control.Holds.Waiting = () => atTheGate.TrySetResult();

        var resume = control.Resume(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await resuming.Task.WaitAsync(RunWorld.Token);
        var press = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await Task.WhenAny(atTheGate.Task, press).WaitAsync(RunWorld.Token);
        finish.SetResult();

        (await resume).Refusal.Should().Be(SandboxControlRefusal.None);
        (await press).Refusal.Should().Be(SandboxControlRefusal.None);
        Pointer(provider).Should().ContainSingle("the press came after the resume began, so it let go of nothing")
            .Which.Should().Contain("mousedown");
    }

    [Fact]
    public async Task A_move_or_a_release_that_overlaps_a_pause_sends_nothing_and_leaves_the_row_paused()
    {
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        provider.BeforePause = async (_, cancellationToken) =>
        {
            pausing.TrySetResult();
            await finish.Task.WaitAsync(cancellationToken);
        };

        var pause = control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await pausing.Task.WaitAsync(RunWorld.Token);
        control.Holds.Waiting = () => atTheGate.TrySetResult();
        var move = control.Input(world.SpaceId, world.UserId, world.BotId, new("move", X: 7, Y: 8), RunWorld.Token);
        var up = control.Input(world.SpaceId, world.UserId, world.BotId, new("up", X: 7, Y: 8), RunWorld.Token);
        await Task.WhenAny(atTheGate.Task, move).WaitAsync(RunWorld.Token);
        finish.SetResult();

        (await pause).Refusal.Should().Be(SandboxControlRefusal.None);
        var moved = await move;
        var lifted = await up;
        moved.Problem.Should().Contain("paused or replaced");
        lifted.Problem.Should().Contain("paused or replaced");
        Pointer(provider).Should().HaveCount(2, "only the press and the pause's release reached the display");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
        (await world.Read(db => db.Sandboxes.OrderBy(row => row.Id).LastAsync(RunWorld.Token))).State
            .Should().Be(SandboxState.Paused, "no input that overlapped the pause recorded a use afterwards");
    }

    [Fact]
    public async Task A_press_whose_audit_row_cannot_be_written_is_let_go_of_and_the_row_is_tried_once()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var attempts = 0;
        control.AuditWriting = () =>
        {
            attempts++;
            throw new InvalidOperationException("The audit log could not be written.");
        };

        var pressing = () => control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);

        await pressing.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1, "the attempt has one row, written once");
        Pointer(provider).Should().HaveCount(2);
        Pointer(provider)[1].Should().Equal("mouseup", "1");
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeFalse();
    }

    [Fact]
    public async Task A_pause_ended_while_it_looks_for_its_sandbox_does_not_refuse_a_press_that_was_waiting()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) => command.Arguments is [.., "sleep 1"]
            ? HoldCommand(running, release, token)
            : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var command = control.Run(world.SpaceId, world.UserId, world.BotId, "sleep 1", RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        broker.LeaseHeld = () => leaseHeld.TrySetResult();
        var press = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 7, Y: 8), RunWorld.Token);
        await leaseHeld.Task.WaitAsync(RunWorld.Token);
        using var request = new CancellationTokenSource();
        broker.BeforePowerLookup = _ => request.CancelAsync();

        var pause = () => control.Pause(world.SpaceId, world.UserId, world.BotId, request.Token);

        await pause.Should().ThrowAsync<OperationCanceledException>();
        broker.BeforePowerLookup = null;
        release.SetResult();
        await command;
        (await press).Refusal.Should().Be(SandboxControlRefusal.None, "the pause ended before it asked for anything");
        provider.Pauses.Should().BeEmpty();
        Pointer(provider).Should().ContainSingle().Which.Should().Contain("mousedown");
    }

    [Fact]
    public async Task A_rebuild_whose_sandbox_was_taken_out_of_use_meanwhile_makes_nothing()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        control.AfterFirstLookup = async () =>
        {
            control.AfterFirstLookup = null;
            await SetSandboxRow(world, row =>
            {
                row.State = SandboxState.Retiring;
                row.RetireAfter = DateTimeOffset.UtcNow.AddDays(1);
            });
        };

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        rebuilt.Refusal.Should().Be(SandboxControlRefusal.Refused);
        rebuilt.Problem.Should().Contain("changed while this was under way");
        provider.Created.Should().Be(1, "a rebuild whose view is stale makes no sandbox");
    }

    [Fact]
    public async Task A_bots_use_of_the_sandbox_lets_go_of_a_persons_button_first_even_one_still_being_pressed()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) =>
            IsPress(command) ? HoldCommand(running, release, token) : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var press = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);

        var clicking = broker.Act(BotCall(world), DesktopCommands.Click(1, 1, MouseButton.Left, false), "click there",
            RunWorld.Token);
        release.SetResult();
        (await press).Refusal.Should().Be(SandboxControlRefusal.None);
        await clicking;

        Pointer(provider)
            .Select(arguments => arguments.Contains("mousedown") ? "press"
                : arguments.Contains("mouseup") ? "release"
                : "click")
            .Should().Equal(["press", "release", "click"], "a bot never works with a person's button down");
    }

    [Fact]
    public async Task Input_past_the_spaces_bound_is_refused_before_reaching_the_provider_and_letting_go_still_goes()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) =>
            command.Program == "xdotool" && command.Arguments.Contains("click")
                ? HoldCommand(running, release, token)
                : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        List<Task<SandboxControlResult<bool>>> underWay =
        [
            .. Enumerable.Range(0, SandboxControlService.MaxInputsPerSpace).Select(_ =>
                control.Input(world.SpaceId, world.UserId, world.BotId, new("click", X: 1, Y: 1), RunWorld.Token)),
        ];
        await running.Task.WaitAsync(RunWorld.Token);
        var sent = provider.Commands.Count;

        var refused = await control.Input(world.SpaceId, world.UserId, world.BotId, new("move", X: 2, Y: 2),
            RunWorld.Token);

        refused.Refusal.Should().Be(SandboxControlRefusal.Refused);
        refused.Problem.Should().Contain("Too much input");
        provider.Commands.Count.Should().Be(sent, "nothing was looked up or sent for it");
        var letting = control.Input(world.SpaceId, world.UserId, world.BotId, new("up"), RunWorld.Token);
        release.SetResult();
        (await Task.WhenAll(underWay)).Should().OnlyContain(result => result.Refusal == SandboxControlRefusal.None);
        (await letting).Refusal.Should().Be(SandboxControlRefusal.None, "letting go has room of its own");
    }

    #endregion

    #region Round Thirteen: Ownership Until A Confirmed Release, Authority At Dispatch, Bound Rebuilds

    [Fact]
    public async Task A_press_whose_audit_row_is_slow_is_let_go_of_as_soon_as_the_row_is_written_after_its_deadline()
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.AuditWriting = async () =>
        {
            writing.TrySetResult();
            await finish.Task;
        };

        var pressing = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await writing.Task.WaitAsync(RunWorld.Token);
        clock.Advance(SandboxPointerHolds.Limit);
        Pointer(provider).Should().ContainSingle("the press still has the screen while its row is written");
        finish.SetResult();
        (await pressing).Refusal.Should().Be(SandboxControlRefusal.None);
        await control.Holds.Quiet();

        Pointer(provider).Should().HaveCount(2, "the deadline passed while the row was written");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeFalse();
    }

    [Fact]
    public async Task A_person_demoted_while_their_input_waits_for_the_sandbox_sends_nothing()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = PointerProvider((command, token) =>
            command.Program == "xdotool" && command.Arguments.Contains("click")
                ? HoldCommand(running, release, token)
                : null);
        await using var world = await ControlWorld(provider, desktop: true);
        var admin = await world.AddMember(MembershipRole.Admin);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var clicking = control.Input(world.SpaceId, world.UserId, world.BotId, new("click", X: 1, Y: 1),
            RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        broker.LeaseHeld = () => queued.TrySetResult();

        var pressing = control.Input(world.SpaceId, admin, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        await queued.Task.WaitAsync(RunWorld.Token);
        await world.Read(async db =>
        {
            var membership = await db.Memberships.SingleAsync(
                row => row.SpaceId == world.SpaceId && row.UserId == admin,
                RunWorld.Token);
            membership.Role = MembershipRole.Member;
            return true;
        });
        release.SetResult();
        (await clicking).Refusal.Should().Be(SandboxControlRefusal.None);
        var pressed = await pressing;

        pressed.Refusal.Should().NotBe(SandboxControlRefusal.None);
        pressed.Problem.Should().Contain("Owners and admins");
        Pointer(provider).Should().NotContain(
            arguments => arguments.Contains("mousedown"),
            "the person was no longer an admin when their press reached the screen");
    }

    [Fact]
    public async Task A_release_that_fails_after_a_press_woke_a_paused_sandbox_is_tried_again_on_that_sandbox()
    {
        var releases = 0;
        var provider = PointerProvider((command, _) =>
            IsPress(command) || (IsRelease(command) && Interlocked.Increment(ref releases) is 1)
                ? Failing("exit")
                : null);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal
            .Should().Be(SandboxControlRefusal.None);

        var pressed = await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);
        pressed.Refusal.Should().Be(SandboxControlRefusal.Refused);
        Pointer(provider).Should().HaveCount(2, "the press and its first release, which failed");
        await Advance(clock, control, SandboxPointerHolds.Limit);

        Pointer(provider).Should().HaveCount(3, "the row still reads as paused, but the provider says it runs");
        Pointer(provider)[2].Should().Equal("mouseup", "1");
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeFalse();
    }

    [Fact]
    public async Task A_button_on_a_sandbox_whose_destruction_failed_is_kept_and_let_go_of_there_once_it_can_be()
    {
        var letting = false;
        var releasedOn = new ConcurrentQueue<string>();
        var provider = PointerProvider((command, _) => IsRelease(command) && !Volatile.Read(ref letting)
            ? Failing("exit")
            : null);
        var desktop = provider.Behaviour;
        provider.Behaviour = (command, token, cancellationToken) =>
        {
            if (IsRelease(command) && Volatile.Read(ref letting))
            {
                releasedOn.Enqueue(provider.WithToken(token).Id);
            }

            return desktop(command, token, cancellationToken);
        };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        var old = await SandboxId(world);
        var oldDisplay = provider.Only().Id;
        provider.FailingDestroys[oldDisplay] = true;

        (await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal
            .Should().Be(SandboxControlRefusal.None);
        control.Holds.IsHeldOn(world.SpaceId, old).Should().BeTrue(
            "the old display could not be destroyed, so its button may still be down");
        Volatile.Write(ref letting, true);
        await Advance(clock, control, SandboxPointerHolds.Limit);

        releasedOn.Should().Equal([oldDisplay], "it is let go of on the display it was pressed on");
        control.Holds.IsHeldOn(world.SpaceId, old).Should().BeFalse();
    }

    [Fact]
    public async Task A_rebuild_whose_bot_now_shares_another_groups_sandbox_makes_nothing_for_that_group()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var group = Guid.Empty;
        broker.BeforeRebuildCreate = async _ =>
        {
            broker.BeforeRebuildCreate = null;
            group = await MoveUnderGroup(world);
        };

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        rebuilt.Refusal.Should().Be(SandboxControlRefusal.Refused);
        rebuilt.Problem.Should().Contain("changed while this was under way");
        (await world.Read(db => db.Sandboxes.CountAsync(row => row.OwnerBotId == group, RunWorld.Token)))
            .Should().Be(0, "no row is made for a group the rebuild did not check");
        provider.Created.Should().Be(1);
    }

    [Fact]
    public async Task A_rebuild_whose_sandbox_another_call_took_out_of_use_first_makes_nothing()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        broker.BeforeRetire = async _ =>
        {
            broker.BeforeRetire = null;
            await SetSandboxRow(world, row =>
            {
                row.State = SandboxState.Retiring;
                row.RetireAfter = DateTimeOffset.UtcNow.AddDays(1);
            });
        };

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        rebuilt.Refusal.Should().Be(SandboxControlRefusal.Refused);
        rebuilt.Problem.Should().Contain("changed while this was under way");
        provider.Created.Should().Be(1, "the retirement it expected to make was already made, so it makes nothing");
    }

    [Fact]
    public async Task A_rebuild_leaves_a_new_sandbox_another_call_is_making_to_that_call()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var theirs = Guid.Empty;
        broker.BeforeRebuildCreate = async _ =>
        {
            broker.BeforeRebuildCreate = null;
            theirs = await world.Read(db =>
            {
                var row = new Sandbox
                {
                    OwnerBotId = world.BotId,
                    Policy = SandboxPolicy.Private,
                    Provider = provider.Kind,
                    ProviderSandboxId = string.Empty,
                    State = SandboxState.Creating,
                };
                db.Sandboxes.Add(row);
                return Task.FromResult(row.Id);
            });
        };

        var rebuilt = await control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        rebuilt.Refusal.Should().Be(SandboxControlRefusal.None);
        provider.Created.Should().Be(1, "the other call is making that sandbox; this rebuild does not make it again");
        (await world.Read(db => db.Sandboxes.SingleAsync(row => row.Id == theirs, RunWorld.Token))).State
            .Should().Be(SandboxState.Creating);
    }

    #endregion

    #region Round Fourteen: Authority Right Before Dispatch, Role Loss, The Bot's Screenshot

    [Fact]
    public async Task A_person_demoted_while_a_due_button_is_let_go_of_before_their_input_sends_nothing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        RunWorld? made = null;
        var admin = Guid.Empty;
        var demoted = 0;
        var provider = DesktopProvider();
        var desktop = provider.Behaviour;
        provider.Behaviour = (command, token, cancellationToken) =>
            IsRelease(command) && Interlocked.Exchange(ref demoted, 1) is 0
                ? DemoteThenRun(() => Demote(made!, admin), desktop(command, token, cancellationToken))
                : desktop(command, token, cancellationToken);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        made = world;
        admin = await world.AddMember(MembershipRole.Admin);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token);
        var resumed = 0;
        provider.BeforeResume = (_, _) =>
        {
            // The owner's button comes due while the admin's click acquires the sandbox, so the gate lets go of it
            // after the acquisition, and the admin is demoted while that release runs.
            if (Interlocked.Exchange(ref resumed, 1) is 0)
            {
                clock.Advance(SandboxPointerHolds.Limit);
            }

            return Task.CompletedTask;
        };

        var clicked = await control.Input(world.SpaceId, admin, world.BotId, new("click", X: 1, Y: 1), RunWorld.Token);
        await control.Holds.Quiet();

        clicked.Refusal.Should().Be(SandboxControlRefusal.Refused);
        clicked.Problem.Should().Contain("Owners and admins");
        demoted.Should().Be(1, "the demotion happened during the release after the sandbox was acquired");
        provider.Commands.Should().NotContain(
            command => command.Program == "xdotool" && command.Arguments.Contains("click"),
            "the role is read again right before the input is sent");
    }

    [Fact]
    public async Task A_person_who_lost_their_role_has_their_held_button_let_go_of_at_their_next_input()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var admin = await world.AddMember(MembershipRole.Admin);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Input(world.SpaceId, admin, world.BotId, new("down", X: 5, Y: 6), RunWorld.Token)).Refusal
            .Should().Be(SandboxControlRefusal.None);
        await Demote(world, admin);

        var moved = await control.Input(world.SpaceId, admin, world.BotId, new("move", X: 7, Y: 8), RunWorld.Token);

        moved.Refusal.Should().NotBe(SandboxControlRefusal.None);
        Pointer(provider).Should().HaveCount(2, "the press, then its release at once, and no move");
        Pointer(provider)[1].Should().Equal("mouseup", "1");
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeFalse();
    }

    [Fact]
    public async Task A_bots_screenshot_holds_the_command_lock_so_no_person_press_comes_before_its_capture()
    {
        var shooting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = false;
        var provider = DesktopProvider(async cancellationToken =>
        {
            if (Volatile.Read(ref holding))
            {
                shooting.TrySetResult();
                await shot.Task.WaitAsync(cancellationToken);
            }
        });
        await using var world = await ControlWorld(
            provider,
            desktop: true,
            locks: new SandboxCommandLimits { MaxQueuedPerSandbox = 1 });
        var control = Control(world);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        await broker.Screenshot(BotCall(world), RunWorld.Token);
        provider.Commands.Clear();
        Volatile.Write(ref holding, true);

        var screenshot = broker.Screenshot(BotCall(world), RunWorld.Token);
        await shooting.Task.WaitAsync(RunWorld.Token);
        var pressed = await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);
        shot.SetResult();
        await screenshot;

        pressed.Refusal.Should().Be(SandboxControlRefusal.Refused, "the bot's capture had the sandbox's command lock");
        Pointer(provider).Should().BeEmpty("no press reached the screen while the bot was taking its picture");
    }

    #endregion

    #region The Power Controls Under The Gate

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("rebuild")]
    [InlineData("turn off")]
    public async Task A_power_control_whose_caller_is_demoted_while_it_waits_for_the_screen_does_nothing(string power)
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presses = 0;
        var provider = PointerProvider((command, token) =>
            IsPress(command) && Interlocked.Increment(ref presses) is 2
                ? HoldCommand(running, finish, token)
                : null);
        // The clock stays where it is, so no held button comes due and is let go of while the test looks.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var admin = await world.AddMember(MembershipRole.Admin);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Input(world.SpaceId, admin, world.BotId, new("down", X: 1, Y: 2, Button: "right"),
            RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        var pressing = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        var resumes = 0;
        provider.BeforeResume = (_, _) =>
        {
            Interlocked.Increment(ref resumes);
            return Task.CompletedTask;
        };
        var sent = provider.Commands.Count;
        var created = provider.Created;
        control.Holds.Waiting = () => atTheGate.TrySetResult();

        var acting = power switch
        {
            "pause" => control.Pause(world.SpaceId, admin, world.BotId, RunWorld.Token),
            "resume" => control.Resume(world.SpaceId, admin, world.BotId, RunWorld.Token),
            "rebuild" => control.Rebuild(world.SpaceId, admin, world.BotId, RunWorld.Token),
            _ => control.TurnOff(world.SpaceId, admin, world.BotId, RunWorld.Token),
        };
        await atTheGate.Task.WaitAsync(RunWorld.Token);
        control.Holds.Waiting = null;
        await Demote(world, admin);
        finish.SetResult();
        (await pressing).Refusal.Should().Be(SandboxControlRefusal.None);
        var result = await acting;

        result.Refusal.Should().Be(SandboxControlRefusal.NotPermitted, "the caller was demoted while it waited");
        provider.Pauses.Should().BeEmpty();
        provider.Destroys.Should().BeEmpty();
        provider.Created.Should().Be(created, "nothing was made");
        resumes.Should().Be(0, "nothing was resumed");
        provider.Commands.Skip(sent).Should().NotContain(
            command => command.Program == "xdotool" && command.Arguments.Contains("mouseup"),
            "a refused control lets go of nothing; the held buttons stay with their own deadlines");
        (await world.Read(db => db.Sandboxes.OrderBy(row => row.Id).LastAsync(RunWorld.Token))).State
            .Should().Be(SandboxState.Running, "nothing was paused or retired");
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeTrue();
        (await world.Read(db => db.AuditLog.AsNoTracking()
            .Where(entry => entry.ActorId == admin && entry.Action.StartsWith("sandbox.")
                && entry.Action != "sandbox.input")
            .Select(entry => entry.Data)
            .ToListAsync(RunWorld.Token))).Should().ContainSingle()
            .Which.GetProperty("outcome").GetString().Should().Be("refused");
        await LetGoOfEverything(world, control);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("rebuild")]
    [InlineData("turn off")]
    public async Task A_power_control_whose_bot_moved_to_another_sandbox_while_it_waited_lets_go_of_nothing(string power)
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var atTheGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presses = 0;
        var provider = PointerProvider((command, token) =>
            IsPress(command) && Interlocked.Increment(ref presses) is 2
                ? HoldCommand(running, finish, token)
                : null);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        (await control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 1, Y: 2, Button: "right"),
            RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        var pressing = control.Input(world.SpaceId, world.UserId, world.BotId, new("down", X: 5, Y: 6),
            RunWorld.Token);
        await running.Task.WaitAsync(RunWorld.Token);
        var sent = provider.Commands.Count;
        control.Holds.Waiting = () => atTheGate.TrySetResult();

        var acting = power switch
        {
            "pause" => control.Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
            "rebuild" => control.Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
            _ => control.TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token),
        };
        await atTheGate.Task.WaitAsync(RunWorld.Token);
        control.Holds.Waiting = null;
        await MoveUnderGroup(world);
        finish.SetResult();
        (await pressing).Refusal.Should().Be(SandboxControlRefusal.None);
        var result = await acting;

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        result.Problem.Should().Contain("changed while this was under way");
        provider.Commands.Skip(sent).Should().NotContain(
            command => command.Program == "xdotool" && command.Arguments.Contains("mouseup"),
            "the old display's buttons are left to their own deadlines, not let go of for a control that does nothing");
        provider.Pauses.Should().BeEmpty();
        provider.Destroys.Should().BeEmpty();
        control.Holds.IsHeldOn(world.SpaceId, await SandboxId(world)).Should().BeTrue();
        await LetGoOfEverything(world, control);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("rebuild")]
    [InlineData("turn off")]
    public async Task A_power_control_whose_caller_is_demoted_after_the_gate_checks_asks_the_provider_nothing(
        string power)
    {
        var provider = DesktopProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await ControlWorld(provider, desktop: true, clock: clock);
        var admin = await world.AddMember(MembershipRole.Admin);
        var control = Control(world);
        await StartDesktop(world, control, provider);
        var broker = world.Hosts[^1].Services.GetRequiredService<SandboxBroker>();
        var resumes = 0;
        provider.BeforeResume = (_, _) =>
        {
            Interlocked.Increment(ref resumes);
            return Task.CompletedTask;
        };
        var created = provider.Created;

        // The gate's own check has passed; the broker then looks for the sandbox, and the demotion lands there.
        broker.BeforePowerLookup = async _ =>
        {
            broker.BeforePowerLookup = null;
            await Demote(world, admin);
        };
        var result = power switch
        {
            "pause" => await control.Pause(world.SpaceId, admin, world.BotId, RunWorld.Token),
            "resume" => await control.Resume(world.SpaceId, admin, world.BotId, RunWorld.Token),
            "rebuild" => await control.Rebuild(world.SpaceId, admin, world.BotId, RunWorld.Token),
            _ => await control.TurnOff(world.SpaceId, admin, world.BotId, RunWorld.Token),
        };

        broker.BeforePowerLookup.Should().BeNull("the demotion happened during the broker's lookup");
        result.Refusal.Should().Be(SandboxControlRefusal.NotPermitted);
        provider.Pauses.Should().BeEmpty();
        provider.Destroys.Should().BeEmpty();
        provider.Created.Should().Be(created);
        resumes.Should().Be(0);
        (await world.Read(db => db.Sandboxes.OrderBy(row => row.Id).LastAsync(RunWorld.Token))).State
            .Should().Be(SandboxState.Running, "the row was not paused or taken out of use");
    }

    #endregion

    #region Terminal

    [Fact]
    public async Task A_command_makes_the_sandbox_on_first_use_and_returns_its_output_scrubbed_of_the_token()
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (_, token, _) => Print($"hello {token}\n", "warned\n", 3);
        await using var world = await ControlWorld(provider);

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "env", RunWorld.Token);

        result.Value.Should().Be(new SandboxCommandResponse(3, $"hello {Redactor.Mark}\n", "warned\n", Truncated: false, TimedOut: false, NewSandbox: false));
        provider.Created.Should().Be(1);
        provider.Commands.Single().Arguments.Should().Equal("-l", "-c", "env");
    }

    [Fact]
    public async Task A_persons_command_is_stopped_at_the_deployments_command_limit()
    {
        var provider = new FakeSandboxProvider { Behaviour = (command, _, cancellationToken) => RunUntilTimeLimit(command, cancellationToken) };
        await using var world = await ControlWorld(provider, policy: DeploymentPolicy.SelfHosted with { MaxSandboxCommandSeconds = 1 });

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "sleep 600", RunWorld.Token);

        result.Value!.TimedOut.Should().BeTrue();
        result.Value.ExitStatus.Should().BeNull();
        provider.Commands.Single().Timeout.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("echo \u0000")]
    public async Task An_empty_or_unreadable_command_is_refused_before_the_sandbox_is_touched(string command)
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, command, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task A_command_longer_than_the_limit_is_refused()
    {
        await using var world = await ControlWorld(new FakeSandboxProvider());

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, new string('a', SandboxControlService.MaxCommandCharacters + 1), RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Invalid);
    }

    [Fact]
    public async Task A_sandbox_set_to_open_network_is_refused_where_the_deployment_forbids_it()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider, network: NetworkAccess.Open, policy: DeploymentPolicy.Hosted);

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "ls", RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        result.Problem.Should().Contain(nameof(DeploymentPolicy.AllowOpenSandboxNetwork));
        provider.Created.Should().Be(0);
    }

    #endregion

    #region Who May

    [Theory]
    [InlineData(MembershipRole.Member, SandboxControlRefusal.NotPermitted)]
    [InlineData(MembershipRole.Admin, SandboxControlRefusal.None)]
    public async Task Only_a_person_who_may_change_the_bot_may_use_its_sandbox(MembershipRole role, SandboxControlRefusal expected)
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var person = await world.AddMember(role);
        var control = Control(world);

        var results = new[]
        {
            (await control.GetStatus(world.SpaceId, person, world.BotId, RunWorld.Token)).Refusal,
            (await control.Run(world.SpaceId, person, world.BotId, "true", RunWorld.Token)).Refusal,
            (await control.Pause(world.SpaceId, person, world.BotId, RunWorld.Token)).Refusal,
            (await control.Rebuild(world.SpaceId, person, world.BotId, RunWorld.Token)).Refusal,
            (await control.TurnOff(world.SpaceId, person, world.BotId, RunWorld.Token)).Refusal,
        };

        results.Should().AllBeEquivalentTo(expected);
        provider.Created.Should().Be(expected is SandboxControlRefusal.None ? 2 : 0);
    }

    [Fact]
    public async Task A_person_outside_the_space_or_in_another_space_finds_no_bot_and_touches_nothing()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var control = Control(world);
        var stranger = Guid.CreateVersion7();
        var otherSpace = Guid.CreateVersion7();
        await world.TransactAs(otherSpace, world.UserId, services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.Spaces.Add(new Space { Id = otherSpace, Kind = SpaceKind.Personal, Name = "Other", Slug = $"other-{otherSpace:N}" });
            db.Memberships.Add(new Membership { UserId = world.UserId, Role = MembershipRole.Owner });
            return Task.FromResult(true);
        });

        var notMember = await control.Run(world.SpaceId, stranger, world.BotId, "true", RunWorld.Token);
        var fromOtherSpace = await control.Run(otherSpace, world.UserId, world.BotId, "true", RunWorld.Token);
        var noSuchBot = await control.Run(world.SpaceId, world.UserId, Guid.CreateVersion7(), "true", RunWorld.Token);

        notMember.Refusal.Should().Be(SandboxControlRefusal.NotFound);
        fromOtherSpace.Refusal.Should().Be(SandboxControlRefusal.NotFound, "row-level security hides the other space's bot");
        noSuchBot.Refusal.Should().Be(SandboxControlRefusal.NotFound);
        provider.Created.Should().Be(0);
        provider.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_a_person_does_stores_the_sandbox_token_outside_the_vault()
    {
        var provider = DesktopProvider();
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await control.Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("click", X: 1, Y: 1), RunWorld.Token);
        var token = provider.Only().Token;

        var status = await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        var audit = await world.Read(db => db.AuditLog.AsNoTracking().Select(entry => entry.Data).ToListAsync(RunWorld.Token));

        status.Value!.ToString().Should().NotContain(token).And.NotContain(provider.Only().Id);
        audit.Should().OnlyContain(data => !data.GetRawText().Contains(token) && !data.GetRawText().Contains(provider.Only().Id));
    }

    #endregion

    #region Audit

    [Fact]
    public async Task A_command_the_provider_fails_is_still_written_to_the_audit_log_as_refused()
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (_, _, _) => FailAfterStarting();
        await using var world = await ControlWorld(provider);

        var result = await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "echo hi", RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        result.Problem.Should().Be("The sandbox provider is not available right now. Try again in a moment.");
        (await Actions(world)).Should().Equal("sandbox.command");
        (await Outcomes(world)).Should().Equal("refused");
    }

    [Fact]
    public async Task A_command_whose_request_is_cancelled_is_still_written_to_the_audit_log()
    {
        var provider = new FakeSandboxProvider();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Behaviour = (_, _, cancellationToken) => HoldCommand(running, release, cancellationToken);
        await using var world = await ControlWorld(provider);
        using var caller = new CancellationTokenSource();
        var run = Control(world).Run(world.SpaceId, world.UserId, world.BotId, "sleep 60", caller.Token);
        try
        {
            await running.Task.WaitAsync(RunWorld.Token);
            await caller.CancelAsync();
            await FluentActions.Awaiting(() => run).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.TrySetResult();
        }

        (await Actions(world)).Should().Equal("sandbox.command");
        (await Outcomes(world)).Should().Equal("cancelled");
    }

    [Fact]
    public async Task An_audit_row_is_written_even_when_the_request_ended_before_the_write()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        using var caller = new CancellationTokenSource();
        var control = Control(world);
        control.WorkCompleted = () => caller.Cancel();

        var result = await control.Run(world.SpaceId, world.UserId, world.BotId, "true", caller.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.None);
        (await Actions(world)).Should().Equal("sandbox.command");
        (await Outcomes(world)).Should().Equal("done");
    }

    [Fact]
    public async Task A_rebuild_that_could_not_make_the_new_sandbox_is_written_to_the_audit_log()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        provider.BeforeCreate = (_, _) => throw new SandboxProviderException(SandboxFailure.Unavailable, "No sandbox now.");

        var result = await Control(world).Rebuild(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        (await Actions(world)).Should().Equal("sandbox.command", "sandbox.rebuilt");
        (await Outcomes(world)).Should().Equal("done", "refused");
    }

    [Fact]
    public async Task A_turn_off_the_provider_refused_is_written_to_the_audit_log()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        await Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token);
        provider.FailingDestroys[provider.Only().Id] = true;

        var result = await Control(world).TurnOff(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);

        result.Refusal.Should().Be(SandboxControlRefusal.Refused);
        (await Actions(world)).Should().Equal("sandbox.command", "sandbox.turned_off");
        (await Outcomes(world)).Should().Equal("done", "refused");
    }

    [Fact]
    public async Task An_unexpected_failure_in_the_broker_is_written_as_failed_and_rethrown()
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (_, _, _) => FailUnexpectedly();
        await using var world = await ControlWorld(provider);

        var failure = await FluentActions.Awaiting(() => Control(world).Run(world.SpaceId, world.UserId, world.BotId, "true", RunWorld.Token))
            .Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Message.Should().Be("Something else went wrong.");
        (await Actions(world)).Should().Equal("sandbox.command");
        (await Outcomes(world)).Should().Equal("failed");
    }

    [Fact]
    public async Task A_refusal_before_the_sandbox_is_reached_writes_nothing()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await ControlWorld(provider);
        var member = await world.AddMember(MembershipRole.Member);
        var control = Control(world);

        (await control.Run(world.SpaceId, member, world.BotId, "true", RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.NotPermitted);
        (await control.Run(world.SpaceId, world.UserId, world.BotId, " ", RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.Invalid);
        (await control.Run(world.SpaceId, Guid.CreateVersion7(), world.BotId, "true", RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.NotFound);

        (await Actions(world)).Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static async IAsyncEnumerable<CommandOutput> FailAfterStarting()
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        throw new SandboxProviderException(SandboxFailure.Unavailable, "The provider's own detail.");
    }

    private static async IAsyncEnumerable<CommandOutput> FailUnexpectedly()
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        throw new InvalidOperationException("Something else went wrong.");
    }

    private static async IAsyncEnumerable<CommandOutput> HoldCommand(
        TaskCompletionSource running,
        TaskCompletionSource release,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        running.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
        yield return new CommandExited(0, null);
    }

    private static async Task<List<string?>> Outcomes(RunWorld world)
    {
        var data = await world.Read(db => db.AuditLog.AsNoTracking()
            .Where(entry => entry.Action.StartsWith("sandbox."))
            .OrderBy(entry => entry.Id)
            .Select(entry => entry.Data)
            .ToListAsync(RunWorld.Token));

        return [.. data.Select(entry => entry.GetProperty("outcome").GetString())];
    }


    private async Task<RunWorld> ControlWorld(
        FakeSandboxProvider provider,
        SandboxPolicy sandbox = SandboxPolicy.Private,
        bool desktop = false,
        NetworkAccess network = NetworkAccess.Off,
        DeploymentPolicy? policy = null,
        TimeProvider? clock = null,
        SandboxCommandLimits? locks = null)
    {
        var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();

                if (locks is not null)
                {
                    services.AddSingleton(services => new SandboxCommandLocks(
                        AppConnectionString(services),
                        services.GetRequiredService<ILogger<SandboxCommandLocks>>(),
                        locks));
                }

                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }

                if (policy is not null)
                {
                    services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                }
            });
        await GiveSandbox(world, sandbox, desktop, network);
        return world;
    }

    private static Task<bool> GiveSandbox(RunWorld world, SandboxPolicy policy, bool desktop, NetworkAccess network = NetworkAccess.Off) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = policy;
            bot.SandboxSettings = new SandboxSettings { Network = network, Desktop = desktop };
            return true;
        });

    private static SandboxControlService Control(RunWorld world) =>
        (world.Hosts.Count > 1 ? world.Hosts[^1] : world.StartHost()).Services.GetRequiredService<SandboxControlService>();

    /// <summary>Commits a retirement between the provider's pause and the broker's write to the row.</summary>
    private static async Task<SandboxControlResult<BotSandboxStatus>> PauseAfterRetiring(
        FakeSandboxProvider provider,
        RunWorld world,
        Func<Task> retire)
    {
        var pausing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.BeforePause = async (_, cancellationToken) =>
        {
            pausing.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };

        var pause = Control(world).Pause(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        try
        {
            await pausing.Task.WaitAsync(RunWorld.Token);
            await retire();
        }
        finally
        {
            release.TrySetResult();
        }

        return await pause;
    }

    private static Task<List<string>> Actions(RunWorld world) =>
        world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action.StartsWith("sandbox.")).OrderBy(entry => entry.Id).Select(entry => entry.Action).ToListAsync(RunWorld.Token));

    /// <summary>The desktop already made, and its commands so far forgotten, so a test reads only what it
    /// sent.</summary>
    private static async Task StartDesktop(RunWorld world, SandboxControlService control, FakeSandboxProvider provider)
    {
        await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        provider.Commands.Clear();
    }

    /// <summary>The arguments of every pointer command so far: those of xdotool that press, move, let go, or
    /// drag.</summary>
    private static List<string[]> Pointer(FakeSandboxProvider provider) =>
        [.. provider.Commands.Where(command => command.Program == "xdotool" && command.Arguments.Intersect(["mousedown",
            "mouseup", "mousemove"]).Any()).Select(command => command.Arguments.ToArray())];

    private static bool IsPress(CommandRequest command) => command.Program == "xdotool"
        && command.Arguments.Contains("mousedown") && !command.Arguments.Contains("mouseup");

    private static bool IsDrag(CommandRequest command) => command.Program == "xdotool"
        && command.Arguments.Contains("mousedown") && command.Arguments.Contains("mouseup");

    private static bool IsRelease(CommandRequest command) => command.Program == "xdotool"
        && command.Arguments.Contains("mouseup") && !command.Arguments.Contains("mousedown");

    /// <summary>A desktop whose commands are answered by <paramref name="pointer"/> when it has an answer, and by a
    /// working desktop when not.</summary>
    private static FakeSandboxProvider PointerProvider(Func<CommandRequest, CancellationToken,
        IAsyncEnumerable<CommandOutput>?> pointer)
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (command, token, cancellationToken) => pointer(command, cancellationToken)
            ?? Desktop(provider.WithToken(token), command, null, cancellationToken);
        return provider;
    }

    /// <summary>A command that works, and ends the request that sent it as it does.</summary>
    private static async IAsyncEnumerable<CommandOutput> Cancelling(CancellationTokenSource request)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        await request.CancelAsync();
        yield return new CommandExited(0, null);
    }

    private static IAsyncEnumerable<CommandOutput> Failing(string failure) => failure switch
    {
        "provider" => FailAfterStarting(),
        "unexpected" => FailUnexpectedly(),
        _ => ExitWith(1),
    };

    private static async IAsyncEnumerable<CommandOutput> ExitWith(int exitCode)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        yield return new CommandExited(exitCode, null);
    }

    /// <summary>A command that runs until the provider cancels it.</summary>
    private static IAsyncEnumerable<CommandOutput> Holding(TaskCompletionSource running, TaskCompletionSource never,
        CancellationToken cancellationToken) =>
        HoldCommand(running, never, cancellationToken);

    /// <summary>Puts the bot under a new bot that shares one sandbox with its subtree, and answers its id.</summary>
    private static Task<Guid> MoveUnderGroup(RunWorld world) =>
        world.Read(async db =>
        {
            var group = new Bot
            {
                Name = "group",
                Handle = $"group{Guid.NewGuid():N}"[..12],
                ModelConnectionId = world.ConnectionId,
                Model = "scripted",
                Depth = 0,
                SandboxPolicy = SandboxPolicy.Subtree,
                SandboxSettings = new SandboxSettings { Desktop = true },
            };
            db.Bots.Add(group);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = group.Id, DescendantBotId = group.Id, Distance = 0 });
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.ParentBotId = group.Id;
            bot.Depth = 1;
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = group.Id, DescendantBotId = bot.Id, Distance = 1 });
            return group.Id;
        });

    /// <summary>The connection string the host's own database context uses.</summary>
    private static string AppConnectionString(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DroverDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("The host's database has no connection string.");
    }

    /// <summary>
    /// Lets go of every button held on the sandbox the test used, by its row, so nothing is held when a world on a
    /// fixed clock is disposed: the server's stop would otherwise wait for a release on a clock that never moves.
    /// </summary>
    private static async Task LetGoOfEverything(RunWorld world, SandboxControlService control) =>
        (await control.Holds.ReleaseAll(world.SpaceId, await SandboxId(world), RunWorld.Token))
            .Should().BeTrue("the test lets go of what it held before the world is disposed");

    /// <summary>Makes the person a member who may not use the bot's sandbox.</summary>
    private static Task<bool> Demote(RunWorld world, Guid userId) =>
        world.Read(async db =>
        {
            var membership = await db.Memberships.SingleAsync(
                row => row.SpaceId == world.SpaceId && row.UserId == userId,
                RunWorld.Token);
            membership.Role = MembershipRole.Member;
            return true;
        });

    /// <summary>Runs <paramref name="first"/>, then the command's own output.</summary>
    private static async IAsyncEnumerable<CommandOutput> DemoteThenRun(
        Func<Task> first,
        IAsyncEnumerable<CommandOutput> then)
    {
        await first();

        await foreach (var output in then)
        {
            yield return output;
        }
    }

    private static Task<Guid> SandboxId(RunWorld world) =>
        world.Read(async db => (await db.Sandboxes.OrderBy(row => row.Id).LastAsync(RunWorld.Token)).Id);

    /// <summary>A bot's tool call outside a run's conversation, as the broker is handed one.</summary>
    private static BuiltinToolCall BotCall(RunWorld world) =>
        new(
            JsonSerializer.SerializeToElement(new { }),
            world.BotId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    private static async Task SetSandboxRow(RunWorld world, Action<Sandbox> change) =>
        await world.Read(async db =>
        {
            change(await db.Sandboxes.OrderBy(row => row.Id).LastAsync(RunWorld.Token));
            return true;
        });

    /// <summary>
    /// Moves the clock and waits until every release that the move started has ended. The clock never moves under a
    /// release that is running, since a release has its own deadline on the same clock.
    /// </summary>
    private static async Task Advance(FakeTimeProvider clock, SandboxControlService control, TimeSpan by)
    {
        clock.Advance(by);
        await control.Holds.Quiet();
    }

    private static async Task AdvanceTimes(FakeTimeProvider clock, SandboxControlService control, int times)
    {
        for (var time = 0; time < times; time++)
        {
            await Advance(clock, control, SandboxPointerHolds.Limit);
        }
    }

    /// <summary>
    /// A provider whose sandbox behaves like a desktop: the display check passes, scrot writes a PNG to the path it is
    /// given once <paramref name="shooting"/> lets it, rm removes it, and xdotool does what it is told.
    /// </summary>
    private static FakeSandboxProvider DesktopProvider(Func<CancellationToken, Task>? shooting = null)
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (command, token, cancellationToken) => Desktop(provider.WithToken(token), command, shooting, cancellationToken);
        return provider;
    }

    private static async IAsyncEnumerable<CommandOutput> Desktop(
        FakeSandboxProvider.FakeSandbox sandbox,
        CommandRequest command,
        Func<CancellationToken, Task>? shooting,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (command.Program == "scrot")
        {
            if (shooting is not null)
            {
                await shooting(cancellationToken);
            }

            sandbox.Files[command.Arguments[^1]] = png;
        }
        else if (command.Program == "rm")
        {
            sandbox.Files.TryRemove(command.Arguments[^1], out _);
        }

        yield return new CommandStarted(1);
        yield return new CommandExited(0, null);
    }

    private static async IAsyncEnumerable<CommandOutput> Print(string stdout, string stderr, int exitCode)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        yield return new CommandStdout(Encoding.UTF8.GetBytes(stdout));
        yield return new CommandStderr(Encoding.UTF8.GetBytes(stderr));
        yield return new CommandExited(exitCode, null);
    }

    /// <summary>A command that never ends by itself: the provider stops it at its time limit, as a real one does.</summary>
    private static async IAsyncEnumerable<CommandOutput> RunUntilTimeLimit(CommandRequest command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new CommandStarted(1);
        await Task.Delay(command.Timeout, cancellationToken);
        throw new SandboxProviderException(SandboxFailure.TimedOut, "The command ran past its time limit.");
    }

    #endregion
}
