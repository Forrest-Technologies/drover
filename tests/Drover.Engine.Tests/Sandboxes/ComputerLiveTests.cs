namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// One real run through the engine against the real E2B service with a desktop sandbox: a scripted model takes a
/// screenshot, clicks, types, presses a key, scrolls, and takes another. The key is in DROVER_TEST_E2B_KEY, the domain
/// in DROVER_TEST_E2B_DOMAIN (default e2b.app). Explicit and marked Live, so the normal run and pull request CI never
/// execute it. The desktop template is a large machine, so the run is short and the sandbox is destroyed at the end,
/// pass or fail; nothing lists and kills. Set DROVER_TEST_SCREENSHOT_DIR to keep the two screenshots for a look.
/// Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.ComputerLiveTests</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class ComputerLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    private const string ScreenshotFolderVariable = "DROVER_TEST_SCREENSHOT_DIR";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_bot_sees_its_desktop_and_drives_it_through_a_real_run()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_screenshot")],
            [new ToolCallOutput("c2", "computer_click", """{"x":440,"y":744}""")],
            [new ToolCallOutput("c3", "computer_scroll", """{"direction":"down","amount":1,"x":600,"y":400}""")],
            [new ToolCallOutput("c4", "computer_type", """{"text":"echo drover-$((6*7))"}""")],
            [new ToolCallOutput("c5", "computer_key", """{"keys":["enter"]}""")],
            [new ToolCallOutput("c6", "computer_screenshot")],
            [new TextOutput("The terminal printed drover-42.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddE2BSandboxProvider(options =>
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live computer test.");
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

        try
        {
            var runId = await world.Post(await world.NewThread(), "Open a terminal on the desktop and run a command in it.");
            var run = await WaitLong(world, runId);

            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
            var calls = (await world.ReadToolCalls(runId)).OrderBy(call => call.StartedAt).ToList();
            calls.Select(call => call.ToolName).Should().Equal(
                "computer_screenshot", "computer_click", "computer_scroll", "computer_type", "computer_key", "computer_screenshot");
            calls.Should().OnlyContain(call => !call.IsError, string.Join("\n", calls.Select(call => call.Result?.GetRawText())));
            JsonElement.DeepEquals(calls[0].Result!.Value, JsonElement.Parse("""{"screenshot":"attached","width":1024,"height":768}""")).Should().BeTrue(calls[0].Result!.Value.GetRawText());

            var images = world.Model.ImagesSeen;
            images.Should().HaveCount(7);
            images.Select(seen => seen.Count).Should().Equal(0, 1, 0, 0, 0, 0, 1);
            var first = images[1].Single();
            var second = images[6].Single();
            first.MediaType.Should().Be("image/png");
            DesktopCommands.ReadPngSize(first.Data.Span).Should().Be((1024, 768));
            DesktopCommands.ReadPngSize(second.Data.Span).Should().Be((1024, 768));
            first.Data.Span.SequenceEqual(second.Data.Span).Should().BeFalse("a terminal opened and printed what was typed");
            Keep(first, second);

            var stored = await world.Read(db => db.Messages.AsNoTracking().Where(message => message.ThreadId == run.ThreadId).ToListAsync(RunWorld.Token));
            stored.SelectMany(message => message.Parts).Should().NotContain(part => part is ImagePart, "a screenshot is shown once and never stored");
            var row = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
            Note($"Run {runId} completed; desktop sandbox {row.ProviderSandboxId}; screenshots of {first.Data.Length} and {second.Data.Length} bytes");
        }
        finally
        {
            await DestroyCreated(world, host);
        }
    }

    #endregion

    #region Private Methods

    private static async Task<Run> WaitLong(RunWorld world, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await world.ReadRun(runId);

            if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled or RunStatus.Waiting)
            {
                return run;
            }

            (watch.Elapsed < TimeSpan.FromMinutes(5)).Should().BeTrue("a live desktop run should finish within five minutes");
            await Task.Delay(500, RunWorld.Token);
        }
    }

    private static void Keep(ModelData first, ModelData second)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFolderVariable) is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "computer-live-1.png"), first.Data.ToArray());
        File.WriteAllBytes(Path.Combine(folder, "computer-live-2.png"), second.Data.ToArray());
        Note($"Screenshots kept in {folder}");
    }

    /// <summary>Destroys exactly the sandboxes this test's rows name, through the adapter and its metadata check.</summary>
    private static async Task DestroyCreated(RunWorld world, EngineHost host)
    {
        var provider = host.Services.GetRequiredService<ISandboxProvider>();
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().Where(sandbox => sandbox.ProviderSandboxId != string.Empty).ToListAsync(CancellationToken.None));

        foreach (var row in rows)
        {
            var token = row.AccessSecretId is { } secretId
                ? await world.Transact(services => services.GetRequiredService<ISecretVault>().Reveal(secretId, CancellationToken.None))
                : string.Empty;
            var handle = new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, token);
            await provider.Destroy(handle, CancellationToken.None);
            var gone = await provider.Describe(handle, CancellationToken.None);
            Note($"Destroyed sandbox {row.ProviderSandboxId}: provider now reports {gone.State}");
            gone.State.Should().Be(ProviderSandboxState.Gone);
        }
    }

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
