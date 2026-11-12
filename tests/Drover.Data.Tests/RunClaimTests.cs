namespace Drover.Data.Tests;

/// <summary>
/// <c>sys.claim_runs(worker, limit)</c> leases runnable root runs across all spaces, each to one caller only. These
/// tests run in a database of their own, because a claim takes every runnable run it finds, whoever made it.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class RunClaimTests(PostgresFixture postgres)
{
    #region Private Fields

    private const int Callers = 50;

    private const int RunsPerSpace = 100;

    #endregion

    #region Concurrency

    [Fact]
    public async Task Fifty_concurrent_callers_never_get_the_same_run_twice()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var inA = await SaveRuns(provider, world.SpaceA, world.UserA1, RunsPerSpace);
        var inB = await SaveRuns(provider, world.SpaceB, world.UserB1, RunsPerSpace);

        var firstWave = await ClaimAtTheSameTime(scratch.AppConnectionString);
        var claimed = firstWave.SelectMany(wave => wave).ToList();
        claimed.Count.Should().BeGreaterThan(0);
        claimed.Select(run => run.RunId).Should().OnlyHaveUniqueItems("no run may be handed to two callers");

        while (await Claim(scratch.AppConnectionString, "drain", 25) is { Count: > 0 } more)
        {
            claimed.AddRange(more);
        }

        claimed.Select(run => run.RunId).Should().BeEquivalentTo([.. inA, .. inB], "every run is claimed, and exactly once");
        claimed.Where(run => inA.Contains(run.RunId)).Should().OnlyContain(run => run.SpaceId == world.SpaceA && run.PrincipalUserId == world.UserA1);
        claimed.Where(run => inB.Contains(run.RunId)).Should().OnlyContain(run => run.SpaceId == world.SpaceB && run.PrincipalUserId == world.UserB1);
    }

    [Fact]
    public async Task Each_claimed_run_is_leased_to_the_worker_that_received_it()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var saved = await SaveRuns(provider, world.SpaceA, world.UserA1, 60);

        var waves = await ClaimAtTheSameTime(scratch.AppConnectionString);
        var receivedBy = waves.Select((wave, caller) => (Worker: $"worker-{caller}", Wave: wave))
            .SelectMany(pair => pair.Wave.Select(run => (run.RunId, pair.Worker)))
            .ToDictionary(pair => pair.RunId, pair => pair.Worker);

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var leases = await session.Db.Runs
            .Where(run => saved.Contains(run.Id) && run.Status == RunStatus.Running)
            .ToDictionaryAsync(run => run.Id, run => run.LeaseOwner, TestContext.Current.CancellationToken);

        leases.Should().BeEquivalentTo(receivedBy);
    }

    #endregion

    #region What Is Claimable

    [Fact]
    public async Task Only_runnable_root_runs_are_claimed_and_each_is_leased_for_forty_five_seconds()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var set = await SaveEveryState(provider, world);

        var claimed = await Claim(scratch.AppConnectionString, "worker-1", 100);

        claimed.Select(run => run.RunId).Should().BeEquivalentTo(
            [set.Queued.Id, set.ExpiredLease.Id, set.AnsweredWaiting.Id, set.StoppedWaiting.Id],
            "a waiting run somebody asked to stop is claimed so a worker can end it, answer or no answer");
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var runs = await session.Db.Runs.ToDictionaryAsync(run => run.Id, TestContext.Current.CancellationToken);

        runs[set.Queued.Id].Should().Match<Run>(run => run.Status == RunStatus.Running && run.LeaseOwner == "worker-1" && run.Attempt == 0 && run.StartedAt != null);
        runs[set.ExpiredLease.Id].Attempt.Should().Be(2, "taking over a lost lease is another attempt");
        runs[set.AnsweredWaiting.Id].Should().Match<Run>(run => run.Status == RunStatus.Running && run.StatusReason == null && run.Attempt == 0);
        runs[set.Queued.Id].LeaseExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(45), TimeSpan.FromSeconds(30));
        runs[set.LiveLease.Id].Should().Match<Run>(run => run.LeaseOwner == "someone-else");
        runs[set.PendingApproval.Id].Status.Should().Be(RunStatus.Waiting);
        runs[set.Child.Id].Status.Should().Be(RunStatus.Queued, "a child run is executed by the worker that holds its root");
        runs[set.Completed.Id].Status.Should().Be(RunStatus.Completed);
    }

    [Fact]
    public async Task A_limit_of_zero_or_less_claims_nothing()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await SaveRuns(provider, world.SpaceA, world.UserA1, 3);

        (await Claim(scratch.AppConnectionString, "worker-1", 0)).Should().BeEmpty();
        (await Claim(scratch.AppConnectionString, "worker-1", -5)).Should().BeEmpty();
        (await Claim(scratch.AppConnectionString, "worker-1", 2)).Should().HaveCount(2);
    }

    #endregion

    #region Private Methods

    private static async Task<List<Guid>> SaveRuns(IServiceProvider provider, Guid spaceId, Guid userId, int count)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var runs = Enumerable.Range(0, count).Select(_ => CoreRows.NewRun(bot, userId)).ToList();
        session.Db.Runs.AddRange(runs);
        await session.Commit();
        return [.. runs.Select(run => run.Id)];
    }

    /// <summary>Opens every connection first and then lets all callers go at once, so their statements overlap.</summary>
    private static async Task<List<List<ClaimedRun>>> ClaimAtTheSameTime(string connectionString)
    {
        var ready = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        var waiting = 0;

        var callers = Enumerable.Range(0, Callers).Select(caller => Task.Run(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            if (Interlocked.Increment(ref waiting) == Callers)
            {
                ready.SetResult();
            }

            await go.Task;
            return await Claim(connection, $"worker-{caller}", 5);
        })).ToList();

        await ready.Task;
        go.SetResult();
        return [.. await Task.WhenAll(callers)];
    }

    private static async Task<List<ClaimedRun>> Claim(string connectionString, string worker, int limit)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await Claim(connection, worker, limit);
    }

    private static async Task<List<ClaimedRun>> Claim(NpgsqlConnection connection, string worker, int limit)
    {
        await using var command = new NpgsqlCommand(
            "SELECT run_id, space_id, principal_user_id FROM sys.claim_runs(@worker, @limit)",
            connection);
        command.Parameters.AddWithValue("worker", worker);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        var claimed = new List<ClaimedRun>();

        while (await reader.ReadAsync())
        {
            claimed.Add(new ClaimedRun(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        }

        return claimed;
    }

    private static async Task<EveryState> SaveEveryState(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        var tool = CoreRows.NewTool();
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        session.Db.ModelConnections.Add(connection);
        session.Db.Tools.Add(tool);
        CoreRows.AddBot(session.Db, bot, []);
        session.Db.Threads.Add(thread);

        var set = new EveryState(
            Queued: CoreRows.NewRun(bot, world.UserA1),
            LiveLease: Leased(CoreRows.NewRun(bot, world.UserA1, RunStatus.Running), DateTimeOffset.UtcNow.AddMinutes(5)),
            ExpiredLease: Leased(CoreRows.NewRun(bot, world.UserA1, RunStatus.Running), DateTimeOffset.UtcNow.AddMinutes(-5)),
            PendingApproval: CoreRows.NewRun(bot, world.UserA1, RunStatus.Waiting),
            AnsweredWaiting: CoreRows.NewRun(bot, world.UserA1, RunStatus.Waiting),
            StoppedWaiting: CoreRows.NewRun(bot, world.UserA1, RunStatus.Waiting),
            Completed: CoreRows.NewRun(bot, world.UserA1, RunStatus.Completed),
            Child: CoreRows.NewRun(bot, world.UserA1));
        set.ExpiredLease.Attempt = 1;
        set.AnsweredWaiting.StatusReason = "awaiting_approval";
        set.StoppedWaiting.CancelRequestedAt = DateTimeOffset.UtcNow;
        set.Child.ParentRunId = set.Queued.Id;
        set.Child.RootRunId = set.Queued.Id;
        session.Db.Runs.AddRange(set.Queued, set.LiveLease, set.ExpiredLease, set.PendingApproval, set.AnsweredWaiting, set.StoppedWaiting, set.Completed);
        await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        session.Db.Runs.Add(set.Child);
        session.Db.ToolCalls.AddRange(
            NewToolCall(set.PendingApproval, thread, tool, ToolCallStatus.PendingApproval),
            NewToolCall(set.StoppedWaiting, thread, tool, ToolCallStatus.PendingApproval),
            NewToolCall(set.AnsweredWaiting, thread, tool, ToolCallStatus.Approved));
        await session.Commit();
        return set;
    }

    private static Run Leased(Run run, DateTimeOffset expires)
    {
        run.LeaseOwner = "someone-else";
        run.LeaseExpiresAt = expires;
        return run;
    }

    private static ToolCall NewToolCall(Run run, ChatThread thread, Tool tool, ToolCallStatus status) => new()
    {
        RunId = run.Id,
        ThreadId = thread.Id,
        AudienceUserId = thread.AudienceUserId,
        ToolId = tool.Id,
        ToolName = "example",
        Arguments = CoreRows.EmptyJson(),
        Status = status,
    };

    #endregion

    #region Nested Types

    private sealed record ClaimedRun(Guid RunId, Guid SpaceId, Guid PrincipalUserId);

    private sealed record EveryState(
        Run Queued,
        Run LiveLease,
        Run ExpiredLease,
        Run PendingApproval,
        Run AnsweredWaiting,
        Run StoppedWaiting,
        Run Completed,
        Run Child);

    #endregion
}
