namespace Drover.Data.Tests;

/// <summary>
/// A read marker is its person's alone: no other member reads it or writes one for them, even on a thread they all
/// share. The migration that added markers marked every thread seen by every member who could see it.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ThreadReadTests(PostgresFixture postgres)
{
    #region Privacy

    [Fact]
    public async Task On_a_shared_thread_each_member_reads_and_moves_only_their_own_marker()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var thread = await InsertThread(provider, world.SpaceA, world.UserA1, bot.Id, shared: true);

        await MarkSeen(provider, world.SpaceA, world.UserA1, thread);
        await MarkSeen(provider, world.SpaceA, world.UserA2, thread);

        (await MarkersVisibleTo(provider, world.SpaceA, world.UserA1)).Should().Equal(world.UserA1);
        (await MarkersVisibleTo(provider, world.SpaceA, world.UserA2)).Should().Equal(world.UserA2);
        await using var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
        var forged = () => asA2.Db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO thread_reads (space_id, thread_id, audience_user_id, seen_through_position, seen_at)
            VALUES (sys.current_space(), {thread.Id}, {world.UserA1}, 99, now())
            """,
            Token);
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    #endregion

    #region Backfill

    [Fact]
    public async Task The_migration_marks_every_thread_seen_by_every_member_who_can_see_it_and_nobody_else()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var botInB = await SaveBot(provider, world.SpaceB, world.UserB1);
        await MigrateTo(scratch, "MemoryNotes");

        var privateToA1 = await InsertOldThread(provider, world.SpaceA, world.UserA1, bot.Id, shared: false, messages: 3);
        var shared = await InsertOldThread(provider, world.SpaceA, world.UserA2, bot.Id, shared: true, messages: 2);
        var empty = await InsertOldThread(provider, world.SpaceA, world.UserA2, bot.Id, shared: false, messages: 0);
        var inB = await InsertOldThread(provider, world.SpaceB, world.UserB1, botInB.Id, shared: false, messages: 1);
        await MigrateTo(scratch, null);

        (await MarkersOf(provider, world.SpaceA, world.UserA1)).Should().BeEquivalentTo([(privateToA1, 3), (shared, 2)]);
        (await MarkersOf(provider, world.SpaceA, world.UserA2)).Should().BeEquivalentTo([(shared, 2), (empty, 0)]);
        (await MarkersOf(provider, world.SpaceA, world.Traveler)).Should().BeEquivalentTo([(shared, 2)], "a member sees the shared thread and nothing private");
        (await MarkersOf(provider, world.SpaceB, world.Traveler)).Should().BeEmpty("the thread in B is private to B1");
        (await MarkersOf(provider, world.SpaceB, world.UserB1)).Should().BeEquivalentTo([(inB, 1)]);
        (await PolicyIsForced(scratch, "thread_reads")).Should().BeTrue();
        (await PolicyIsForced(scratch, "memberships")).Should().BeTrue("the owner reads past the policies only inside the migration");
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

    private static async Task<ChatThread> InsertThread(IServiceProvider provider, Guid spaceId, Guid owner, Guid botId, bool shared)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        var thread = new ChatThread
        {
            BotId = botId,
            OwnerUserId = owner,
            AudienceUserId = shared ? Audience.Everyone : owner,
            LastActivityAt = DateTimeOffset.UtcNow,
        };
        session.Db.Threads.Add(thread);
        await session.Commit();
        return thread;
    }

    /// <summary>A thread and its messages written through the application role, before markers existed.</summary>
    private static async Task<Guid> InsertOldThread(IServiceProvider provider, Guid spaceId, Guid owner, Guid botId, bool shared, int messages)
    {
        var id = Guid.CreateVersion7();
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        await session.Execute(
            """
            INSERT INTO threads (id, space_id, bot_id, owner_user_id, audience_user_id, origin, last_activity_at)
            VALUES (@id, @space, @bot, @owner, @audience, 'chat', now())
            """,
            ("id", id),
            ("space", spaceId),
            ("bot", botId),
            ("owner", owner),
            ("audience", shared ? Audience.Everyone : owner));

        for (var position = 1; position <= messages; position++)
        {
            await session.Execute(
                """
                INSERT INTO messages (id, space_id, thread_id, audience_user_id, position, role, author_user_id, parts)
                VALUES (@id, @space, @thread, @audience, @position, 'user', @owner, '[]'::jsonb)
                """,
                ("id", Guid.CreateVersion7()),
                ("space", spaceId),
                ("thread", id),
                ("audience", shared ? Audience.Everyone : owner),
                ("position", position),
                ("owner", owner));
        }

        await session.Commit();
        return id;
    }

    private static async Task MarkSeen(IServiceProvider provider, Guid spaceId, Guid userId, ChatThread thread)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        await new ThreadRepository(session.Db).MarkSeen(thread, userId, DateTimeOffset.UtcNow, Token);
        await session.Commit();
    }

    private static async Task<List<Guid>> MarkersVisibleTo(IServiceProvider provider, Guid spaceId, Guid userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return await session.Db.ThreadReads.AsNoTracking().Select(read => read.AudienceUserId).ToListAsync(Token);
    }

    private static async Task<List<(Guid Thread, int Through)>> MarkersOf(IServiceProvider provider, Guid spaceId, Guid userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var reads = await session.Db.ThreadReads.AsNoTracking()
            .Where(read => read.AudienceUserId == userId)
            .ToListAsync(Token);
        return [.. reads.Select(read => (read.ThreadId, read.SeenThroughPosition))];
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

    private static async Task<bool> PolicyIsForced(ScratchDatabase scratch, string table)
    {
        await using var connection = new NpgsqlConnection(scratch.AppConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SELECT relforcerowsecurity FROM pg_class WHERE relname = @table", connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync(Token))!;
    }

    #endregion
}
