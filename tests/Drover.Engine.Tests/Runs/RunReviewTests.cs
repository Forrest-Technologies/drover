namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Defects the second review of the run engine found, each held by a test that fails without its fix.
/// </summary>
public sealed class RunReviewTests(PostgresServer postgres)
{
    #region Fencing

    [Fact]
    public async Task A_live_server_that_claims_its_own_run_again_after_the_lease_lapsed_leaves_one_execution_that_can_write()
    {
        var tools = new TestTools().AddGated("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_write")],
            [new TextOutput("It may have been written.")],
            [new TextOutput("A third call must never be made.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("test.write", GrantEffect.Allow);

        // The server stalls with the tool in flight: its watch does not get to renew or notice anything in time.
        var host = world.StartHost(options => options.WatchInterval = TimeSpan.FromMinutes(5));
        var runId = await world.Post(await world.NewThread(), "Write the file");
        await tools.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        var worker = host.WorkerId;
        await world.Read(db => db.Database.ExecuteSqlAsync(
            $"UPDATE runs SET lease_expires_at = now() - interval '1 second' WHERE id = {runId} AND lease_owner = {worker}",
            RunWorld.Token));

        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        var eventsAtCompletion = await world.ReadEvents(runId);

        // The stale execution's tool returns now. It must find the run is no longer its own and write nothing.
        tools.OpenGate();
        await Task.Delay(1000, RunWorld.Token);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        run.Attempt.Should().Be(1, "the same server claimed the run again, which is a new claim");
        run.LeaseOwner.Should().BeNull();
        script.Requests.Should().HaveCount(2, "the stale execution never reaches another model call");
        tools.Count("test.write").Should().Be(1);
        (await world.ReadToolCalls(runId)).Should().ContainSingle()
            .Which.Status.Should().Be(ToolCallStatus.Interrupted, "the new claim settled the call, and the stale execution may not overwrite it");
        var events = await world.ReadEvents(runId);
        events.Should().HaveCount(eventsAtCompletion.Count, "nothing is appended to the log by a claim that lost the run");
        events[^1].Type.Should().Be(RunEventTypes.RunCompleted);
        events.Count(stored => stored.Type == RunEventTypes.RunCompleted).Should().Be(1);
        (await world.ReadRun(runId)).Status.Should().Be(RunStatus.Completed);
        (await world.ReadUsage(runId)).Should().HaveCount(2);
    }

    #endregion

    #region A Run That Cannot Be Loaded

    [Fact]
    public async Task A_run_whose_model_key_cannot_be_read_fails_at_once_and_frees_its_thread()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await world.Read(db => db.Database.ExecuteSqlAsync($"UPDATE secrets SET ciphertext = decode('00112233445566778899', 'hex')", RunWorld.Token));
        world.StartHost();
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "Hello");
        var run = await world.WaitForStatus(runId, RunStatus.Failed, RunStatus.Completed);
        var next = await world.PostRaw(threadId, "Hello again");

        run.Status.Should().Be(RunStatus.Failed);
        run.Error!.Code.Should().Be("model_unavailable");
        run.Attempt.Should().Be(0, "the run fails the first time, it is not claimed over and over");
        script.Requests.Should().BeEmpty();
        (await world.ReadEvents(runId))[^1].Type.Should().Be(RunEventTypes.RunFailed);
        next.Problem.Should().Be(PostProblem.None, "the thread takes its next message once the run has ended");
    }

    [Fact]
    public async Task A_run_whose_load_throws_is_cancelled_when_asked_and_fails_when_it_is_out_of_attempts()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var broken = world.StartHost(replaceServices: services => services.AddScoped<ISecretVault, BrokenVault>());
        var cancelledRun = await world.Post(await world.NewThread(), "Hello");
        var failedRun = await world.Post(await world.NewThread(), "Hello");
        await WaitForLease(world, cancelledRun);
        await WaitForLease(world, failedRun);

        // One is cancelled. Both leases lapse, as they do when a load throws and nothing renews them.
        (await world.Cancel(cancelledRun, broken)).Outcome.Should().Be(CancelOutcome.Requested);

        for (var lapse = 0; lapse < 3; lapse++)
        {
            await world.Read(db => db.Database.ExecuteSqlAsync(
                $"UPDATE runs SET lease_expires_at = now() - interval '1 second' WHERE status = 'running'",
                RunWorld.Token));
            await Task.Delay(700, RunWorld.Token);
        }

        var cancelled = await world.WaitForStatus(cancelledRun, RunStatus.Cancelled, RunStatus.Failed);
        var failed = await world.WaitForStatus(failedRun, RunStatus.Failed, RunStatus.Cancelled);

        cancelled.Status.Should().Be(RunStatus.Cancelled, "a cancel request is honoured even when the run cannot be loaded");
        failed.Status.Should().Be(RunStatus.Failed);
        failed.Error!.Code.Should().Be("internal");
        failed.Attempt.Should().BeLessThan(3, "the run is failed when it runs out of attempts, not claimed for ever");
        (await world.ReadEvents(failedRun))[^1].Type.Should().Be(RunEventTypes.RunFailed);
        (await world.ReadEvents(cancelledRun))[^1].Type.Should().Be(RunEventTypes.RunCancelled);
    }

    #endregion

    #region Default Grants

    [Fact]
    public async Task A_new_bot_is_granted_the_listed_tools_only_and_not_every_built_in_that_reads()
    {
        var tools = new TestTools().Add("test.read_everything", ToolSensitivity.Read);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), tools);

        var botId = await world.Transact(async services =>
            (await services.GetRequiredService<BotService>().Create("Second", string.Empty, world.ConnectionId, null, world.UserId, RunWorld.Token)).Bot!.Id);
        var granted = await world.Transact(async services =>
            (await services.GetRequiredService<ToolRepository>().ListGrantedTools(botId, RunWorld.Token)).Select(tool => tool.Tool.Name).ToList());

        BuiltinTools.GrantedToNewBots.Should().Equal("time.now");
        granted.Should().Equal("time.now");
    }

    #endregion

    #region Private Methods

    private static async Task WaitForLease(RunWorld world, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while ((await world.ReadRun(runId)).LeaseOwner is null)
        {
            (watch.Elapsed < TimeSpan.FromSeconds(30)).Should().BeTrue("the run should be claimed");
            await Task.Delay(25, RunWorld.Token);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>A vault that cannot be reached, so every load of a run with a key throws.</summary>
    private sealed class BrokenVault : ISecretVault
    {
        #region Interface Implementations

        public Task<Guid> Store(SecretKind kind, string value, Guid? privateToUserId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The vault is unavailable.");

        public Task Replace(Guid secretId, string value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The vault is unavailable.");

        public Task<string> Reveal(Guid secretId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The vault is unavailable.");

        public Task Delete(Guid secretId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The vault is unavailable.");

        #endregion
    }

    #endregion
}
