namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// A person's sandbox controls through <see cref="SandboxControlService"/> against the real E2B service with a desktop
/// sandbox: a command from the terminal makes the sandbox, then pause, resume, the screen, a click, typed text and a
/// key, a second screen that differs, rebuild (the old sandbox is gone at the provider and the next use is on a new
/// one), and turn off (gone). The key is in DROVER_TEST_E2B_KEY, the domain in DROVER_TEST_E2B_DOMAIN (default
/// e2b.app). Explicit and marked Live, so the normal run and pull request CI never execute it. The desktop template is
/// a large machine, so the run is short and every sandbox it made is destroyed at the end, pass or fail; nothing lists
/// and kills. Set DROVER_TEST_SCREENSHOT_DIR to keep the two screenshots for a look. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.SandboxControlLiveTests</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class SandboxControlLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    private const string ScreenshotFolderVariable = "DROVER_TEST_SCREENSHOT_DIR";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_person_runs_a_command_pauses_resumes_sees_and_drives_the_screen_rebuilds_and_turns_off_a_real_sandbox()
    {
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddE2BSandboxProvider(options =>
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live sandbox control test.");
                    options.Domain = Environment.GetEnvironmentVariable(DomainVariable) ?? options.Domain;
                });
                services.AddSandboxTools();
            });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Off, IdleTimeoutMinutes = 3, Desktop = true };
            return true;
        });
        var host = world.StartHost();
        var control = host.Services.GetRequiredService<SandboxControlService>();
        var provider = host.Services.GetRequiredService<ISandboxProvider>();
        var (space, person, botId) = (world.SpaceId, world.UserId, world.BotId);

        try
        {
            var ran = await control.Run(space, person, botId, "echo drover-person-$((6*7))", RunWorld.Token);
            ran.Problem.Should().BeNull();
            ran.Value!.Should().Match<SandboxCommandResponse>(result => result.ExitStatus == 0 && result.Stdout == "drover-person-42\n");
            var first = await LiveRow(world);
            Note($"The terminal made sandbox {first.ProviderSandboxId}");

            (await control.Pause(space, person, botId, RunWorld.Token)).Value!.State.Should().Be("paused");
            (await Describe(world, provider, first)).Should().Be(ProviderSandboxState.Paused);
            (await control.Resume(space, person, botId, RunWorld.Token)).Value!.State.Should().Be("running");
            (await Describe(world, provider, first)).Should().Be(ProviderSandboxState.Running);

            var before = await control.Screen(space, person, botId, RunWorld.Token);
            before.Problem.Should().BeNull();
            DesktopCommands.ReadPngSize(before.Value!.Png).Should().Be((1024, 768));

            await Input(control, world, new SandboxInputRequest("click", X: 440, Y: 744));
            await Task.Delay(TimeSpan.FromSeconds(3), RunWorld.Token);
            await Input(control, world, new SandboxInputRequest("type", Text: "echo person-$((6*7))"));
            await Input(control, world, new SandboxInputRequest("key", Keys: ["enter"]));
            await Task.Delay(TimeSpan.FromSeconds(2), RunWorld.Token);
            var after = await control.Screen(space, person, botId, RunWorld.Token);
            after.Problem.Should().BeNull();
            after.Value!.Png.AsSpan().SequenceEqual(before.Value.Png).Should().BeFalse("a terminal opened and printed what was typed");
            Keep(before.Value.Png, after.Value.Png);

            (await control.Rebuild(space, person, botId, RunWorld.Token)).Value!.State.Should().Be("running");
            (await Describe(world, provider, first)).Should().Be(ProviderSandboxState.Gone, "the old sandbox is destroyed at the provider");
            var second = await LiveRow(world);
            second.ProviderSandboxId.Should().NotBe(first.ProviderSandboxId);
            var onNew = await control.Run(space, person, botId, "test -e /tmp/nothing-here || echo fresh", RunWorld.Token);
            onNew.Value!.Stdout.Should().Be("fresh\n");
            (await LiveRow(world)).ProviderSandboxId.Should().Be(second.ProviderSandboxId, "the next use is on the new sandbox");
            Note($"Rebuilt: {first.ProviderSandboxId} is gone, {second.ProviderSandboxId} is in use");

            (await control.TurnOff(space, person, botId, RunWorld.Token)).Value!.State.Should().Be("none");
            (await Describe(world, provider, second)).Should().Be(ProviderSandboxState.Gone);
            Note($"Turned off: {second.ProviderSandboxId} is gone");
        }
        finally
        {
            await DestroyCreated(world, provider);
        }
    }

    #endregion

    #region Private Methods

    private static async Task Input(SandboxControlService control, RunWorld world, SandboxInputRequest input)
    {
        var sent = await control.Input(world.SpaceId, world.UserId, world.BotId, input, RunWorld.Token);
        sent.Problem.Should().BeNull(input.Kind);
    }

    private static Task<Sandbox> LiveRow(RunWorld world) =>
        world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(sandbox => sandbox.State == SandboxState.Running, RunWorld.Token));

    private static async Task<ProviderSandboxState> Describe(RunWorld world, ISandboxProvider provider, Sandbox row)
    {
        var info = await provider.Describe(await Handle(world, row), RunWorld.Token);
        return info.State;
    }

    private static async Task<SandboxHandle> Handle(RunWorld world, Sandbox row)
    {
        var current = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(sandbox => sandbox.Id == row.Id, CancellationToken.None));
        var token = current.AccessSecretId is { } secretId
            ? await world.Transact(services => services.GetRequiredService<ISecretVault>().Reveal(secretId, CancellationToken.None))
            : string.Empty;
        return new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, token);
    }

    private static void Keep(byte[] first, byte[] second)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFolderVariable) is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "control-live-1.png"), first);
        File.WriteAllBytes(Path.Combine(folder, "control-live-2.png"), second);
        Note($"Screenshots kept in {folder}");
    }

    /// <summary>Destroys exactly the sandboxes this test's rows name, through the adapter and its metadata check.</summary>
    private static async Task DestroyCreated(RunWorld world, ISandboxProvider provider)
    {
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().Where(sandbox => sandbox.ProviderSandboxId != string.Empty).ToListAsync(CancellationToken.None));

        foreach (var row in rows)
        {
            var handle = await Handle(world, row);
            await provider.Destroy(handle, CancellationToken.None);
            var gone = await provider.Describe(handle, CancellationToken.None);
            Note($"Destroyed sandbox {row.ProviderSandboxId}: provider now reports {gone.State}");
            gone.State.Should().Be(ProviderSandboxState.Gone);
        }
    }

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
