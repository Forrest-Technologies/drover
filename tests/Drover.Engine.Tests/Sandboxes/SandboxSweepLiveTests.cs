namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// A removed bot's sandbox cleared up at the real E2B service: the bot uses its sandbox, the bot is removed, the sweep
/// pauses the sandbox, and a pass after the retention period destroys it. The key is in DROVER_TEST_E2B_KEY, the
/// domain in DROVER_TEST_E2B_DOMAIN (default e2b.app). Explicit and marked Live, so the normal run and pull request CI
/// never execute it. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.SandboxSweepLiveTests</c>.
/// The sandbox is a <c>base</c> one with a two-minute idle time, and is destroyed at the end, pass or fail; nothing
/// lists and kills.
/// </summary>
[Trait("Live", "true")]
public sealed class SandboxSweepLiveTests(PostgresServer postgres)
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task A_removed_bots_sandbox_is_paused_and_then_destroyed_at_the_provider_by_the_sweep()
    {
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Done.")]),
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
        var host = world.StartHost();
        var provider = host.Services.GetRequiredService<ISandboxProvider>();

        try
        {
            var used = await host.Services.GetRequiredService<SandboxBroker>().Run(Call(world), null, "echo used", null, TimeSpan.FromSeconds(60), RunWorld.Token);
            used.Stdout.Should().Be("used\n");
            var row = await ReadSandbox(world);
            row.State.Should().Be(SandboxState.Running);
            var handle = new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, string.Empty);

            await world.Transact(async services =>
                (await services.GetRequiredService<BotService>().Remove(world.BotId, world.UserId, RunWorld.Token)).Bot is not null);
            var paused = await Sweep(world, host, TimeSpan.Zero);
            var whilePaused = await provider.Describe(handle, RunWorld.Token);
            var destroyed = await Sweep(world, host, SandboxSweep.Retention + TimeSpan.FromMinutes(1));
            var afterwards = await provider.Describe(handle, RunWorld.Token);

            paused.Should().Be(1);
            whilePaused.State.Should().Be(ProviderSandboxState.Paused, "a retired sandbox is paused at once");
            destroyed.Should().Be(1);
            afterwards.State.Should().Be(ProviderSandboxState.Gone);
            var after = await ReadSandbox(world);
            after.State.Should().Be(SandboxState.Destroyed);
            after.AccessSecretId.Should().BeNull();
            (await world.Read(db => db.Secrets.AnyAsync(secret => secret.Id == row.AccessSecretId, RunWorld.Token))).Should().BeFalse();
            Note($"Sandbox {row.ProviderSandboxId}: {whilePaused.State} after the bot was removed, {afterwards.State} after the retention period");
        }
        finally
        {
            await DestroyCreated(world, provider);
        }
    }

    #endregion

    #region Private Methods

    private static Task<int> Sweep(RunWorld world, EngineHost host, TimeSpan later) =>
        new SandboxSweep(
                host.Services.GetRequiredService<RunScopes>(),
                host.Services.GetRequiredService<ISandboxProvider>(),
                new LaterTime(later),
                host.Services.GetRequiredService<ILogger<SandboxSweep>>())
            .Sweep(RunWorld.Token);

    private static Task<Sandbox> ReadSandbox(RunWorld world) =>
        world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));

    private static BuiltinToolCall Call(RunWorld world) =>
        new(
            JsonElement.Parse("{}"),
            world.BotId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    /// <summary>Destroys exactly the sandboxes this test's rows name, through the adapter and its metadata check.</summary>
    private static async Task DestroyCreated(RunWorld world, ISandboxProvider provider)
    {
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().Where(sandbox => sandbox.ProviderSandboxId != string.Empty).ToListAsync(CancellationToken.None));

        foreach (var row in rows)
        {
            await provider.Destroy(new SandboxHandle(row.ProviderSandboxId, row.SpaceId, row.OwnerBotId, string.Empty), CancellationToken.None);
        }
    }

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion

    #region Nested Types

    /// <summary>The clock as it will read once <paramref name="later"/> has gone by.</summary>
    private sealed class LaterTime(TimeSpan later) : TimeProvider
    {
        #region Public Methods

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + later;

        #endregion
    }

    #endregion
}
