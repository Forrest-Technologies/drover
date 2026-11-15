namespace Drover.Data.Tests;

/// <summary>
/// Each person has at most one main thread with each bot, decided by a unique index, so two requests that race to
/// open it end with one row. The migration that added the flag chose the main thread for threads that already existed.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class MainThreadTests(PostgresFixture postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(10);

    #endregion

    #region One Per Person

    [Fact]
    public async Task A_person_has_one_main_thread_with_a_bot_and_any_number_of_side_chats()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        await SaveThread(provider, world, world.UserA1, bot, isMain: true);
        await SaveThread(provider, world, world.UserA1, bot, isMain: false);
        await SaveThread(provider, world, world.UserA1, bot, isMain: false);
        await SaveThread(provider, world, world.UserA2, bot, isMain: true);

        var second = () => SaveThread(provider, world, world.UserA1, bot, isMain: true);

        var thrown = await second.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Adding_a_main_thread_stores_it_once_and_another_person_cannot_read_it()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);

        bool first;
        bool again;
        ChatThread? mine;

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var threads = new ThreadRepository(session.Db);
            first = await threads.AddMainThread(bot.Id, world.UserA1, DateTimeOffset.UtcNow, Token);
            again = await threads.AddMainThread(bot.Id, world.UserA1, DateTimeOffset.UtcNow, Token);
            mine = await threads.FindMainThread(bot.Id, world.UserA1, Token);
            await session.Commit();
        }

        await using var other = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
        var theirs = new ThreadRepository(other.Db);

        first.Should().BeTrue();
        again.Should().BeFalse("the person already has a main thread with this bot");
        mine.Should().Match<ChatThread>(thread =>
            thread.IsMain && thread.OwnerUserId == world.UserA1 && thread.AudienceUserId == world.UserA1
            && thread.SpaceId == world.SpaceA && thread.Title == null && thread.Origin == ThreadOrigin.Chat);
        (await theirs.FindMainThread(bot.Id, world.UserA1, Token)).Should().BeNull("a private main thread is its owner's alone");
        (await other.Db.Threads.CountAsync(thread => thread.Id == mine!.Id, Token)).Should().Be(0);
        (await theirs.FindMainThread(bot.Id, world.UserA2, Token)).Should().BeNull("another person's main thread is not theirs");
    }

    [Fact]
    public async Task Two_requests_racing_to_add_a_main_thread_end_with_one_and_the_loser_reads_it_in_its_own_transaction()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        await using var winner = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        await using var loser = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var won = await new ThreadRepository(winner.Db).AddMainThread(bot.Id, world.UserA1, DateTimeOffset.UtcNow, Token);
        var losing = new ThreadRepository(loser.Db).AddMainThread(bot.Id, world.UserA1, DateTimeOffset.UtcNow, Token);
        var waited = await Task.WhenAny(losing, Task.Delay(TimeSpan.FromMilliseconds(300), Token)) != losing;
        await winner.Commit();
        var lost = await losing.WaitAsync(patience, Token);
        var read = await new ThreadRepository(loser.Db).FindMainThread(bot.Id, world.UserA1, Token);
        await loser.Commit();

        won.Should().BeTrue();
        waited.Should().BeTrue("the second insert waits on the first one's row");
        lost.Should().BeFalse();
        read.Should().NotBeNull("the transaction that lost is still usable and sees the row that won");
        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await check.Db.Threads.CountAsync(thread => thread.BotId == bot.Id && thread.IsMain, Token)).Should().Be(1);
    }

    #endregion

    #region Backfill

    [Fact]
    public async Task The_migration_makes_each_persons_latest_open_chat_thread_with_each_bot_their_main_one()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var otherBot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var botInB = await SaveBot(provider, world.SpaceB, world.UserB1);
        await MigrateTo(scratch, "MessageWithheld");
        var now = DateTimeOffset.UtcNow;

        var older = await InsertOldThread(provider, world.SpaceA, world.UserA1, bot.Id, "chat", now.AddHours(-2));
        var latest = await InsertOldThread(provider, world.SpaceA, world.UserA1, bot.Id, "chat", now.AddHours(-1));
        var archived = await InsertOldThread(provider, world.SpaceA, world.UserA1, bot.Id, "chat", now, archived: true);
        var scheduled = await InsertOldThread(provider, world.SpaceA, world.UserA1, bot.Id, "schedule", now);
        var withOtherBot = await InsertOldThread(provider, world.SpaceA, world.UserA1, otherBot.Id, "chat", now.AddHours(-5));
        var sharedByA2 = await InsertOldThread(provider, world.SpaceA, world.UserA2, bot.Id, "chat", now.AddHours(-3), shared: true);
        var inB = await InsertOldThread(provider, world.SpaceB, world.UserB1, botInB.Id, "chat", now.AddHours(-4));
        await MigrateTo(scratch, null);

        (await MainThreadsOf(provider, world.SpaceA, world.UserA1)).Should().BeEquivalentTo([latest, withOtherBot]);
        (await MainThreadsOf(provider, world.SpaceA, world.UserA2)).Should().BeEquivalentTo([sharedByA2]);
        (await MainThreadsOf(provider, world.SpaceB, world.UserB1)).Should().BeEquivalentTo([inB]);
        new[] { older, archived, scheduled }.Should().NotIntersectWith(await MainThreadsOf(provider, world.SpaceA, world.UserA1));
        (await ThreadsPolicyIsForced(scratch)).Should().BeTrue("the owner reads past the policies only inside the migration");
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<Bot> SaveBot(IServiceProvider provider, Guid spaceId, Guid userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        await session.Commit();
        return bot;
    }

    private static async Task SaveThread(IServiceProvider provider, TenantWorld world, Guid owner, Bot bot, bool isMain)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, owner);
        var thread = CoreRows.NewThread(bot, owner, owner);
        thread.IsMain = isMain;
        session.Db.Threads.Add(thread);
        await session.Commit();
    }

    /// <summary>Moves the database to a migration, or to the latest when <paramref name="target"/> is null, as its owner.</summary>
    private static async Task MigrateTo(ScratchDatabase scratch, string? target)
    {
        NpgsqlConnection.ClearAllPools();
        var options = new DbContextOptionsBuilder<DroverDbContext>();
        options.UseDroverNpgsql(scratch.OwnerConnectionString);
        await using var context = new DroverDbContext(options.Options);
        await context.GetService<IMigrator>().MigrateAsync(target, Token);
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>A thread written the way the schema before main threads held one, through the application role.</summary>
    private static async Task<Guid> InsertOldThread(
        IServiceProvider provider,
        Guid spaceId,
        Guid owner,
        Guid botId,
        string origin,
        DateTimeOffset lastActivityAt,
        bool archived = false,
        bool shared = false)
    {
        var id = Guid.CreateVersion7();
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        await session.Execute(
            """
            INSERT INTO threads (id, space_id, bot_id, owner_user_id, audience_user_id, origin, last_activity_at, archived_at)
            VALUES (@id, @space, @bot, @owner, @audience, @origin, @last, @archived)
            """,
            ("id", id),
            ("space", spaceId),
            ("bot", botId),
            ("owner", owner),
            ("audience", shared ? Audience.Everyone : owner),
            ("origin", origin),
            ("last", lastActivityAt),
            ("archived", archived ? lastActivityAt : null));
        await session.Commit();
        return id;
    }

    private static async Task<List<Guid>> MainThreadsOf(IServiceProvider provider, Guid spaceId, Guid owner)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        return await session.Db.Threads.AsNoTracking()
            .Where(thread => thread.IsMain && thread.OwnerUserId == owner)
            .Select(thread => thread.Id)
            .ToListAsync(Token);
    }

    private static async Task<bool> ThreadsPolicyIsForced(ScratchDatabase scratch)
    {
        await using var connection = new NpgsqlConnection(scratch.AppConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SELECT relforcerowsecurity FROM pg_class WHERE oid = 'public.threads'::regclass", connection);
        return (bool)(await command.ExecuteScalarAsync(Token))!;
    }

    #endregion
}
