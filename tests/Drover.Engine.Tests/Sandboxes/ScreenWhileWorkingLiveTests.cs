namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// What a person sees of a bot's screen while the bot drives it, against the real E2B service with a desktop sandbox:
/// a scripted model takes screenshots, clicks, types and presses a key while a person reads the state and asks for the
/// screen once a second, as the screen beside the conversation does. The state must read running with a screen while
/// the bot works, and every picture asked for while it runs must arrive. The key is in DROVER_TEST_E2B_KEY, the domain
/// in DROVER_TEST_E2B_DOMAIN (default e2b.app). Explicit and marked Live, so the normal run and pull request CI never
/// execute it. The sandbox is destroyed at the end, pass or fail. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.ScreenWhileWorkingLiveTests</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class ScreenWhileWorkingLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_person_sees_the_screen_while_the_bot_drives_it()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_screenshot")],
            [new ToolCallOutput("c2", "computer_click", """{"x":440,"y":744}""")],
            [new ToolCallOutput("c3", "computer_screenshot")],
            [new ToolCallOutput("c4", "computer_type", """{"text":"echo drover-$((6*7))"}""")],
            [new ToolCallOutput("c5", "computer_key", """{"keys":["enter"]}""")],
            [new ToolCallOutput("c6", "computer_screenshot")],
            [new TextOutput("Done.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddE2BSandboxProvider(options =>
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live screen test.");
                    options.Domain = Environment.GetEnvironmentVariable(DomainVariable) ?? options.Domain;
                });
                services.AddSandboxTools();
            });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Off, IdleTimeoutMinutes = 3, Desktop = true };
            var model = await db.ModelCatalog.SingleAsync(RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = true };
            return true;
        });

        foreach (var tool in ComputerTools.Names)
        {
            await world.Grant(tool, GrantEffect.Allow);
        }

        var host = world.StartHost();
        var control = host.Services.GetRequiredService<SandboxControlService>();
        List<BotSandboxStatus> whileWorking = [];
        List<(int Width, int Height)> pictures = [];

        try
        {
            var runId = await world.Post(await world.NewThread(), "Use the desktop.");
            var watch = Stopwatch.StartNew();
            var run = await world.ReadRun(runId);

            while (run.Status is not (RunStatus.Completed or RunStatus.Failed) && watch.Elapsed < TimeSpan.FromMinutes(3))
            {
                var status = (await control.GetStatus(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Value!;
                var calls = await world.ReadToolCalls(runId);
                TestContext.Current.SendDiagnosticMessage($"{watch.Elapsed.TotalSeconds:0.0}s {status.State} working={status.BotWorking} calls={string.Join(",", calls.OrderBy(call => call.StartedAt).Select(call => $"{call.ToolName}{(call.IsError ? "!" : string.Empty)}{(call.FinishedAt is null ? "…" : string.Empty)}"))}");

                if (status.BotWorking)
                {
                    whileWorking.Add(status);
                }

                if (status is { Desktop: true, State: "running" })
                {
                    var asked = watch.Elapsed;
                    var shot = await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token);
                    TestContext.Current.SendDiagnosticMessage($"{asked.TotalSeconds:0.0}s screen asked, {watch.Elapsed.TotalSeconds:0.0}s answered: {shot.Problem ?? "picture"}");
                    shot.Problem.Should().BeNull();
                    pictures.Add((shot.Value!.Width, shot.Value.Height));
                }

                await Task.Delay(TimeSpan.FromSeconds(1), RunWorld.Token);
                run = await world.ReadRun(runId);
            }

            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
            whileWorking.Should().OnlyContain(status => status.Desktop && status.DesktopOffered);
            whileWorking.Should().Contain(status => status.State == "running", "the bot made its sandbox and works in it");
            pictures.Should().NotBeEmpty().And.OnlyContain(size => size.Width == 1024 && size.Height == 768);
            TestContext.Current.SendDiagnosticMessage(
                $"States while the bot worked: {string.Join(", ", whileWorking.Select(status => status.State))}; {pictures.Count} pictures");
        }
        finally
        {
            await ComputerLiveTests.DestroyCreated(world, host);
        }
    }

    #endregion
}
