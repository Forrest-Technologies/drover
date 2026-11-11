namespace Drover.Data.Tests;

/// <summary>
/// Events are numbered per root run by <c>UPDATE runs SET last_event_seq = last_event_seq + n ... RETURNING</c> in
/// the transaction that inserts them. The row lock makes sequence order equal commit order, so a reader can never
/// see event 12 before event 11.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class RunEventSequenceTests(PostgresFixture postgres)
{
    #region Private Fields

    private const int Writers = 20;

    private const int EventsPerWriter = 8;

    #endregion

    #region Concurrent Writers

    [Fact]
    public async Task Twenty_concurrent_writers_to_one_root_run_give_a_gapless_sequence_in_commit_order()
    {
        await using var provider = postgres.BuildServices(maxPoolSize: Writers + 5);
        var world = await TenantWorld.Seed(provider);
        var root = await SaveRootRun(provider, world);

        var writing = Task.WhenAll(Enumerable.Range(0, Writers).Select(writer => Task.Run(() => Append(provider, world, root, writer))));
        var snapshots = ReadWhile(provider, world, root, writing);
        await writing;
        var snapshotProblems = await snapshots;

        snapshotProblems.Should().BeEmpty("a reader must only ever see a gapless prefix of the sequence");
        var events = await ReadEvents(provider, world, root);
        events.Select(found => found.Seq).Should().Equal(Enumerable.Range(1, Writers * EventsPerWriter).Select(number => (long)number));
        events.Select(found => found.CreatedAt).Should().BeInAscendingOrder("a later number was committed after an earlier one");
        (await LastEventSeq(provider, world, root)).Should().Be(Writers * EventsPerWriter);
    }

    [Fact]
    public async Task Each_root_run_has_a_sequence_of_its_own()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var first = await SaveRootRun(provider, world);
        var second = await SaveRootRun(provider, world);

        await Append(provider, world, first, writer: 0);
        await Append(provider, world, second, writer: 0);

        (await ReadEvents(provider, world, first)).Select(found => found.Seq).Should().Equal(Enumerable.Range(1, EventsPerWriter).Select(number => (long)number));
        (await ReadEvents(provider, world, second)).Select(found => found.Seq).Should().Equal(Enumerable.Range(1, EventsPerWriter).Select(number => (long)number));
    }

    #endregion

    #region Private Methods

    private static async Task<RootRun> SaveRootRun(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        var run = CoreRows.NewRun(bot, world.UserA1);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        session.Db.Threads.Add(thread);
        session.Db.Runs.Add(run);
        await session.Commit();
        return new RootRun(run.Id, thread.Id);
    }

    /// <summary>One writer: several appends, each its own transaction, as the engine does after every step.</summary>
    private static async Task Append(IServiceProvider provider, TenantWorld world, RootRun root, int writer)
    {
        for (var index = 0; index < EventsPerWriter; index++)
        {
            await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
            var row = await session.Returning(
                "UPDATE runs SET last_event_seq = last_event_seq + 1 WHERE space_id = @space AND id = @run RETURNING last_event_seq",
                ("space", world.SpaceA),
                ("run", root.RunId));
            await InsertEvent(session, world, root, (long)row["last_event_seq"]!);
            await session.Commit();

            if ((writer + index) % 3 == 0)
            {
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>The time comes from <c>clock_timestamp()</c> after the lock is held, so it follows commit order.</summary>
    private static async Task InsertEvent(TenantSession session, TenantWorld world, RootRun root, long seq) =>
        await session.Execute(
            """
            INSERT INTO run_events (space_id, root_run_id, seq, created_at, run_id, thread_id, audience_user_id, type)
            VALUES (@space, @run, @seq, clock_timestamp(), @run, @thread, @audience, 'message.delta')
            """,
            ("space", world.SpaceA),
            ("run", root.RunId),
            ("seq", seq),
            ("thread", root.ThreadId),
            ("audience", world.UserA1));

    /// <summary>Reads the whole sequence again and again while the writers run. Every read must be 1..n with no hole.</summary>
    private static async Task<List<string>> ReadWhile(IServiceProvider provider, TenantWorld world, RootRun root, Task writing)
    {
        var problems = new List<string>();
        long previousCount = 0;

        while (!writing.IsCompleted)
        {
            var seen = (await ReadEvents(provider, world, root)).Select(found => found.Seq).ToList();

            if (!seen.SequenceEqual(Enumerable.Range(1, seen.Count).Select(number => (long)number)))
            {
                problems.Add($"A reader saw {string.Join(",", seen.TakeLast(5))} after {previousCount} events: not a prefix of 1..n.");
            }

            previousCount = Math.Max(previousCount, seen.Count);
        }

        return problems;
    }

    private static async Task<List<StoredEvent>> ReadEvents(IServiceProvider provider, TenantWorld world, RootRun root)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        return await session.Db.RunEvents
            .Where(found => found.RootRunId == root.RunId)
            .OrderBy(found => found.Seq)
            .Select(found => new StoredEvent(found.Seq, found.CreatedAt))
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> LastEventSeq(IServiceProvider provider, TenantWorld world, RootRun root)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        return await session.Db.Runs
            .Where(found => found.Id == root.RunId)
            .Select(found => found.LastEventSeq)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region Nested Types

    private sealed record RootRun(Guid RunId, Guid ThreadId);

    private sealed record StoredEvent(long Seq, DateTimeOffset CreatedAt);

    #endregion
}
