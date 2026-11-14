namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// One real run through the engine against the real E2B service: a scripted model writes a script into the bot's
/// sandbox and runs it. The key is in DROVER_TEST_E2B_KEY, the domain in DROVER_TEST_E2B_DOMAIN (default e2b.app).
/// Explicit and marked Live, so the normal run and pull request CI never execute it. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true"</c>.
/// The engine host here has no deployment-policy outbound handler, so the clients use the machine's own proxy
/// settings, as the adapter's live conformance test does. The sandbox has a two-minute idle time and is destroyed at
/// the end, pass or fail; nothing lists and kills.
/// </summary>
[Trait("Live", "true")]
public sealed class SandboxLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_bot_writes_a_script_into_its_sandbox_and_runs_it_through_a_real_run()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "sandbox_write_file", """{"path":"/home/user/drover-live/hello.py","content":"import sys\nprint('hello from', 6 * 7)\nprint('to stderr', file=sys.stderr)\n"}""")],
            [new ToolCallOutput("c2", "sandbox_run", """{"command":"python3 /home/user/drover-live/hello.py && echo done","timeout_seconds":60}""")],
            [new TextOutput("It printed hello from 42.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddE2BSandboxProvider(options =>
                {
                    options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live sandbox test.");
                    options.Domain = Environment.GetEnvironmentVariable(DomainVariable) ?? options.Domain;
                });
                services.AddSandboxTools();
            });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Off, IdleTimeoutMinutes = 2 };
            return true;
        });
        await world.Grant("sandbox.write_file", GrantEffect.Allow);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        var host = world.StartHost();

        try
        {
            var runId = await world.Post(await world.NewThread(), "Write the script and run it.");
            var run = await WaitLong(world, runId);

            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
            var calls = (await world.ReadToolCalls(runId)).OrderBy(call => call.StartedAt).ToList();
            calls.Select(call => call.ToolName).Should().Equal("sandbox_write_file", "sandbox_run");
            calls.Should().OnlyContain(call => !call.IsError);
            var result = calls[1].Result!.Value;
            result.GetProperty("exit_code").GetInt32().Should().Be(0);
            result.GetProperty("stdout").GetString().Should().Be("hello from 42\ndone\n");
            result.GetProperty("stderr").GetString().Should().Be("to stderr\n");

            var events = await world.ReadEvents(runId);
            var output = events.Where(stored => stored.Type == RunEventTypes.ToolOutput).Select(RunWorld.Data<ToolOutput>).ToList();
            string.Concat(output.Where(piece => piece.Stream == "stdout").Select(piece => piece.Text)).Should().Be("hello from 42\ndone\n");
            output.Should().Contain(piece => piece.Stream == "stderr" && piece.Text.Contains("to stderr"));

            var row = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
            row.Should().Match<Sandbox>(sandbox => sandbox.Provider == "e2b" && sandbox.State == SandboxState.Running && sandbox.OwnerBotId == world.BotId);
            calls.Should().OnlyContain(call => call.SandboxId == row.Id);
            (await world.Read(db => db.Secrets.AsNoTracking().SingleAsync(secret => secret.Id == row.AccessSecretId, RunWorld.Token)))
                .Kind.Should().Be(SecretKind.SandboxToken);
            var token = await world.Transact(services => services.GetRequiredService<ISecretVault>().Reveal(row.AccessSecretId!.Value, RunWorld.Token));
            token.Should().NotBeNullOrEmpty();
            events.Should().OnlyContain(stored => !stored.Data.Contains(token));
            calls.Should().OnlyContain(call => !call.Arguments.GetRawText().Contains(token) && !call.Result!.Value.GetRawText().Contains(token));
            script.Requests.SelectMany(request => request).Should().OnlyContain(message => !message.Summary.Contains(token));
            Note($"Run {runId} completed; sandbox {row.ProviderSandboxId}; {output.Count} tool.output events");
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

            (watch.Elapsed < TimeSpan.FromMinutes(3)).Should().BeTrue("a live run should finish within three minutes");
            await Task.Delay(250, RunWorld.Token);
        }
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
            await provider.Destroy(new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, token), CancellationToken.None);
            var gone = await provider.Describe(new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, token), CancellationToken.None);
            Note($"Destroyed sandbox {row.ProviderSandboxId}: provider now reports {gone.State}");
            gone.State.Should().Be(ProviderSandboxState.Gone);
        }
    }

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
