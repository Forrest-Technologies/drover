namespace Drover.Data.Tests;

/// <summary>
/// <c>sys.claim_due_schedules(at, until, limit)</c> claims the enabled schedules that are due, across all spaces, each
/// to one caller only, and moves each one's <c>next_run_at</c> to the caller's claim. These tests run in a database
/// of their own, because a claim takes every due schedule it finds, whoever made it.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ScheduleClaimTests(PostgresFixture postgres)
{
    #region Private Fields

    private const int Callers = 50;

    private const int SchedulesPerSpace = 100;

    #endregion

    #region Concurrency

    [Fact]
    public async Task Fifty_concurrent_callers_never_claim_the_same_schedule_twice()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var inA = await SaveSchedules(provider, world.SpaceA, world.UserA1, SchedulesPerSpace, now.AddMinutes(-1));
        var inB = await SaveSchedules(provider, world.SpaceB, world.UserB1, SchedulesPerSpace, now.AddMinutes(-1));

        var claimed = (await ClaimAtTheSameTime(scratch.AppConnectionString, now)).SelectMany(wave => wave).ToList();
        claimed.Count.Should().BeGreaterThan(0);
        claimed.Select(due => due.ScheduleId).Should().OnlyHaveUniqueItems("no schedule may be handed to two callers");

        while (await Claim(scratch.AppConnectionString, now, now.AddMinutes(5), 25) is { Count: > 0 } more)
        {
            claimed.AddRange(more);
        }

        claimed.Select(due => due.ScheduleId).Should().BeEquivalentTo([.. inA, .. inB], "every due schedule is claimed, and exactly once");
        claimed.Where(due => inA.Contains(due.ScheduleId)).Should().OnlyContain(due => due.SpaceId == world.SpaceA && due.OwnerUserId == world.UserA1);
        claimed.Where(due => inB.Contains(due.ScheduleId)).Should().OnlyContain(due => due.SpaceId == world.SpaceB && due.OwnerUserId == world.UserB1);
    }

    #endregion

    #region What Is Claimed

    [Fact]
    public async Task Only_enabled_due_schedules_are_claimed_and_each_one_is_moved_to_the_claim()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var claimUntil = new DateTimeOffset(now.AddMinutes(5).UtcTicks / 10 * 10, TimeSpan.Zero);
        var due = await SaveSchedules(provider, world.SpaceA, world.UserA1, 2, now.AddHours(-3));
        var dueNow = await SaveSchedules(provider, world.SpaceA, world.UserA1, 1, now);
        var later = await SaveSchedules(provider, world.SpaceA, world.UserA1, 1, now.AddMinutes(1));
        var off = await SaveSchedules(provider, world.SpaceA, world.UserA1, 1, now.AddMinutes(-1), enabled: false);

        var claimed = await Claim(scratch.AppConnectionString, now, claimUntil, 100);
        var again = await Claim(scratch.AppConnectionString, now.AddMinutes(1), now.AddMinutes(6), 100);

        claimed.Select(entry => entry.ScheduleId).Should().BeEquivalentTo([.. due, .. dueNow]);
        again.Select(entry => entry.ScheduleId).Should().BeEquivalentTo(later, "a claimed schedule is not due again until its claim runs out");
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await session.Db.BotSchedules.ToDictionaryAsync(schedule => schedule.Id, TestContext.Current.CancellationToken);
        stored.Where(pair => due.Contains(pair.Key) || dueNow.Contains(pair.Key)).Should().OnlyContain(pair => pair.Value.NextRunAt == claimUntil);
        stored[off.Single()].NextRunAt.Should().BeCloseTo(now.AddMinutes(-1), TimeSpan.FromMilliseconds(1), "a schedule that is off is never claimed");
    }

    [Fact]
    public async Task A_claim_shorter_than_a_second_or_longer_than_an_hour_or_a_limit_of_zero_claims_nothing()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        await SaveSchedules(provider, world.SpaceA, world.UserA1, 3, now.AddMinutes(-1));

        (await Claim(scratch.AppConnectionString, now, now, 10)).Should().BeEmpty();
        (await Claim(scratch.AppConnectionString, now, now.AddHours(2), 10)).Should().BeEmpty("no caller may hold a schedule back for longer than an hour");
        (await Claim(scratch.AppConnectionString, now, now.AddMinutes(5), 0)).Should().BeEmpty();
        (await Claim(scratch.AppConnectionString, now, now.AddMinutes(5), -2)).Should().BeEmpty();
        (await Claim(scratch.AppConnectionString, now, now.AddMinutes(5), 2)).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_claim_returns_ids_only_and_a_tenant_scope_cannot_read_another_spaces_claimed_schedule()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var inB = (await SaveSchedules(provider, world.SpaceB, world.UserB1, 1, now.AddMinutes(-1))).Single();

        List<DueSchedule> claimed;
        await using (var bypass = await TenantSession.Open(provider, null, null))
        {
            var repository = new BotScheduleRepository(bypass.Db);
            claimed = await repository.ClaimDue(now, now.AddMinutes(5), 10, TestContext.Current.CancellationToken);
            await bypass.Commit();
        }

        claimed.Should().ContainSingle().Which.Should().Be(new DueSchedule(world.SpaceB, inB, world.UserB1));
        await using var inA = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await new BotScheduleRepository(inA.Db).LockSchedule(inB, TestContext.Current.CancellationToken))
            .Should().BeNull("space A's scope never sees space B's schedule, whatever id it is given");
        await using var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1);
        (await new BotScheduleRepository(asB.Db).LockSchedule(inB, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_claim_returns_the_space_the_schedule_and_the_owner_and_nothing_else()
    {
        var columns = await Query(
            postgres.AdminConnectionString,
            """
            SELECT array_agg(a.attname::text ORDER BY a.attnum)
            FROM pg_proc p, unnest(p.proargnames, p.proargmodes) WITH ORDINALITY a(attname, mode, attnum)
            WHERE p.pronamespace = 'sys'::regnamespace AND p.proname = 'claim_due_schedules' AND a.mode = 't'
            """);

        ((string[])columns.Single()[0]!).Should().Equal("space_id", "schedule_id", "owner_user_id");
    }

    #endregion

    #region Private Methods

    private static async Task<List<Guid>> SaveSchedules(
        IServiceProvider provider,
        Guid spaceId,
        Guid userId,
        int count,
        DateTimeOffset nextRunAt,
        bool enabled = true)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var schedules = Enumerable.Range(0, count).Select(_ => new BotSchedule
        {
            BotId = bot.Id,
            OwnerUserId = userId,
            Cron = "0 9 * * *",
            TimeZone = "UTC",
            Prompt = "Brief me.",
            Enabled = enabled,
            NextRunAt = nextRunAt,
        }).ToList();
        session.Db.BotSchedules.AddRange(schedules);
        await session.Commit();
        return [.. schedules.Select(schedule => schedule.Id)];
    }

    /// <summary>Opens every connection first and then lets all callers go at once, so their statements overlap.</summary>
    private static async Task<List<List<DueSchedule>>> ClaimAtTheSameTime(string connectionString, DateTimeOffset now)
    {
        var ready = new TaskCompletionSource();
        var go = new TaskCompletionSource();
        var waiting = 0;

        var callers = Enumerable.Range(0, Callers).Select(_ => Task.Run(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            if (Interlocked.Increment(ref waiting) == Callers)
            {
                ready.SetResult();
            }

            await go.Task;
            return await Claim(connection, now, now.AddMinutes(5), 5);
        })).ToList();

        await ready.Task;
        go.SetResult();
        return [.. await Task.WhenAll(callers)];
    }

    private static async Task<List<DueSchedule>> Claim(string connectionString, DateTimeOffset now, DateTimeOffset until, int limit)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await Claim(connection, now, until, limit);
    }

    private static async Task<List<DueSchedule>> Claim(NpgsqlConnection connection, DateTimeOffset now, DateTimeOffset until, int limit)
    {
        await using var command = new NpgsqlCommand(
            "SELECT space_id, schedule_id, owner_user_id FROM sys.claim_due_schedules(@now, @until, @limit)",
            connection);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("until", until);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        var claimed = new List<DueSchedule>();

        while (await reader.ReadAsync())
        {
            claimed.Add(new DueSchedule(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2)));
        }

        return claimed;
    }

    private static async Task<List<object?[]>> Query(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();

        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }

    #endregion
}
