namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// One real run through the engine against the real E2B service with a desktop sandbox, for a bot whose model cannot
/// read images: the bot takes a screenshot, a second model reads it, and the bot gets words. Both models are scripted,
/// because no model that reads images is at hand where this runs; what is real is the sandbox, its screen, and the
/// picture. The key is in DROVER_TEST_E2B_KEY, the domain in DROVER_TEST_E2B_DOMAIN (default e2b.app). Explicit and
/// marked Live, so the normal run and pull request CI never execute it. The sandbox is destroyed at the end, pass or
/// fail; nothing lists and kills. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.ScreenReaderLiveTests</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class ScreenReaderLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    private const string ReaderKind = "scripted_reader";

    private const string ReaderModel = "reader-model";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_text_only_bot_reads_a_real_desktop_through_a_second_model()
    {
        var bot = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_screenshot", """{"question":"Where is the terminal launcher?"}""")],
            [new TextOutput("The reader described the desktop.")]);
        var reader = new ScriptedModelProvider(new ScriptedModel([new TextOutput("An Xfce desktop with a panel at the bottom.")]), ReaderKind);
        await using var world = await RunWorld.Create(
            postgres,
            bot,
            everyHost: services =>
            {
                services.AddE2BSandboxProvider(options =>
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live screen reader test.");
                    options.Domain = Environment.GetEnvironmentVariable(DomainVariable) ?? options.Domain;
                });
                services.AddSingleton<IModelClientProvider>(reader);
                services.AddSandboxTools();
            });
        var readerConnectionId = await world.Transact(async services =>
        {
            var change = await services.GetRequiredService<ModelConnectionService>().Create(ReaderKind, "Reader", null, RunWorld.NewModelKey(), ReaderModel, RunWorld.Token);
            services.GetRequiredService<DroverDbContext>().ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = change.Connection!.Id,
                Model = ReaderModel,
                DisplayName = "Reader",
                Capabilities = new ModelCapabilities { Vision = true },
            });
            return change.Connection.Id;
        });
        await world.Read(async db =>
        {
            var found = await db.Bots.SingleAsync(row => row.Id == world.BotId, RunWorld.Token);
            found.SandboxPolicy = SandboxPolicy.Private;
            found.SandboxSettings = new SandboxSettings
            {
                Network = NetworkAccess.Off,
                IdleTimeoutMinutes = 3,
                Desktop = true,
                ScreenReader = new ScreenReader(readerConnectionId, ReaderModel),
            };
            var model = await db.ModelCatalog.SingleAsync(entry => entry.Model == "scripted", RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = false };
            return true;
        });

        foreach (var tool in ComputerTools.Names)
        {
            await world.Grant(tool, GrantEffect.Allow);
        }

        var host = world.StartHost();

        try
        {
            var runId = await world.Post(await world.NewThread(), "Look at the desktop.");
            var run = await ComputerLiveTests.WaitLong(world, runId);

            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
            var call = (await world.ReadToolCalls(runId)).Single();
            call.IsError.Should().BeFalse(call.Result?.GetRawText());
            call.Result!.Value.GetProperty("screenshot").GetString().Should().Be("described");
            call.Result.Value.GetProperty("description").GetString().Should().Be("An Xfce desktop with a panel at the bottom.");

            var picture = reader.Requests.Single().Messages.Single().Contents.OfType<ModelData>().Single();
            picture.MediaType.Should().Be("image/png");
            DesktopCommands.ReadPngSize(picture.Data.Span).Should().Be((1024, 768));
            reader.Requests.Single().Options.Instructions.Should().Be(ScreenDescriber.Instruction);
            world.Model.ImagesSeen.Should().HaveCount(2).And.OnlyContain(images => images.Count == 0, "the bot's text-only model gets words only");
            world.Script.Requests.SelectMany(request => request).Should().NotContain(message => message.Summary.StartsWith("image", StringComparison.Ordinal));
            Note($"Run {runId} completed; the reader got a {picture.Data.Length} byte 1024 by 768 PNG and the bot's model got text only");
        }
        finally
        {
            await ComputerLiveTests.DestroyCreated(world, host);
        }
    }

    #endregion

    #region Private Methods

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
