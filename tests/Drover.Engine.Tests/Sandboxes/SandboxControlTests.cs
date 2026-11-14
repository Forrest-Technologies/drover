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

        status.Value.Should().Be(new BotSandboxStatus(Available: true, HasSandbox: true, "none", null, Desktop: false, BotWorking: false));
        provider.Created.Should().Be(0);
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
    public async Task Screens_asked_for_at_the_same_time_share_one_screenshot()
    {
        var provider = DesktopProvider(TimeSpan.FromMilliseconds(300));
        await using var world = await ControlWorld(provider, desktop: true);
        var control = Control(world);
        await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
        provider.Commands.Clear();

        var shots = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)));

        shots.Should().OnlyContain(shot => shot.Value != null && shot.Value.Png.SequenceEqual(png));
        provider.Commands.Count(command => command.Program == "scrot").Should().Be(1);
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
        var command = $"echo command-{Guid.NewGuid():N}";
        await control.Run(world.SpaceId, world.UserId, world.BotId, command, RunWorld.Token);
        await control.Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("type", Text: typed), RunWorld.Token);

        var entries = await world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action.StartsWith("sandbox.")).OrderBy(entry => entry.Id).ToListAsync(RunWorld.Token));

        entries.Select(entry => entry.Action).Should().Equal("sandbox.command", "sandbox.input");
        entries.Should().OnlyContain(entry => entry.ActorId == world.UserId && entry.TargetKind == "bot" && entry.TargetId == world.BotId);
        entries[1].Data.GetProperty("input").GetString().Should().Be("type");
        entries.Should().OnlyContain(entry => !entry.Data.GetRawText().Contains(typed) && !entry.Data.GetRawText().Contains("command-"));
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

    #region Helpers

    private async Task<RunWorld> ControlWorld(
        FakeSandboxProvider provider,
        SandboxPolicy sandbox = SandboxPolicy.Private,
        bool desktop = false,
        NetworkAccess network = NetworkAccess.Off,
        DeploymentPolicy? policy = null)
    {
        var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();

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

    private static Task<List<string>> Actions(RunWorld world) =>
        world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action.StartsWith("sandbox.")).OrderBy(entry => entry.Id).Select(entry => entry.Action).ToListAsync(RunWorld.Token));

    /// <summary>
    /// A provider whose sandbox behaves like a desktop: the display check passes, scrot writes a PNG to the path it is
    /// given after <paramref name="delay"/>, rm removes it, and xdotool does what it is told.
    /// </summary>
    private static FakeSandboxProvider DesktopProvider(TimeSpan? delay = null)
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (command, token, cancellationToken) => Desktop(provider.WithToken(token), command, delay ?? TimeSpan.Zero, cancellationToken);
        return provider;
    }

    private static async IAsyncEnumerable<CommandOutput> Desktop(
        FakeSandboxProvider.FakeSandbox sandbox,
        CommandRequest command,
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (command.Program == "scrot")
        {
            await Task.Delay(delay, cancellationToken);
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
