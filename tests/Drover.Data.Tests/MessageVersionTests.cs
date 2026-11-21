namespace Drover.Data.Tests;

/// <summary>
/// The versions of a conversation are rows of <c>messages</c>: each message names the one before it in its version,
/// one version of each place is shown, and the rest are shelved, never deleted. The database holds one shown message
/// per place and keeps a message's parent in its own space.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class MessageVersionTests(PostgresFixture postgres)
{
    #region Storage

    [Fact]
    public async Task A_new_version_takes_its_place_and_the_shelved_version_comes_back_with_what_followed_it()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await NewThread(provider, world.SpaceA, world.UserA1);
        var (first, reply, next) = (Said(thread, "Hi"), Said(thread, "Hello."), Said(thread, "Bye"));
        await Append(provider, world, thread, first, reply, next);
        var edited = Said(thread, "Hi there");

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var messages = new MessageRepository(session.Db);
            await messages.Shelve(thread.Id, 2, DateTimeOffset.UtcNow, Token);
            messages.AddVersion(thread, reply, edited);
            await session.Commit();
        }

        var afterEdit = await ShownTexts(provider, world, thread);
        var versions = await ListVersions(provider, world, thread);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var messages = new MessageRepository(session.Db);
            await messages.Shelve(thread.Id, 2, DateTimeOffset.UtcNow, Token);
            await messages.Restore(thread.Id, reply.Id, Token);
            await session.Commit();
        }

        reply.ParentId.Should().Be(first.Id);
        next.ParentId.Should().Be(reply.Id);
        afterEdit.Should().Equal("Hi", "Hi there");
        versions.Should().ContainKey(reply.Id).WhoseValue.Should().Equal(reply.Id, edited.Id);
        versions.Should().NotContainKey(first.Id, "a place with one version has nothing to switch to");
        (await ShownTexts(provider, world, thread)).Should().Equal("Hi", "Hello.", "Bye");
    }

    [Fact]
    public async Task Two_shown_messages_cannot_share_a_place_but_a_shelved_one_can()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await NewThread(provider, world.SpaceA, world.UserA1);
        var first = Said(thread, "Hi");
        await Append(provider, world, thread, first);

        var shown = () => Insert(provider, world.SpaceA, world.UserA1, thread, Said(thread, "Again", position: 1));
        var shelved = () => Insert(provider, world.SpaceA, world.UserA1, thread, Said(thread, "Kept", position: 1, shelvedAt: DateTimeOffset.UtcNow));

        (await shown.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        await shelved.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_message_cannot_follow_a_message_of_another_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var inB = await NewThread(provider, world.SpaceB, world.UserB1);
        var foreign = Said(inB, "In B");
        await using (var session = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            await new MessageRepository(session.Db).AppendMessages(inB, [foreign], Token);
            await session.Commit();
        }

        var inA = await NewThread(provider, world.SpaceA, world.UserA1);
        var forged = Said(inA, "Follows B", position: 1);
        forged.ParentId = foreign.Id;
        var insert = () => Insert(provider, world.SpaceA, world.UserA1, inA, forged);

        (await insert.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    #endregion

    #region Backfill

    [Fact]
    public async Task The_migration_links_each_message_to_the_one_before_it_in_its_own_thread()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await MigrateTo(scratch, "ConversationModels");
        var inA = await InsertOldThread(provider, world.SpaceA, world.UserA1, 3);
        var inB = await InsertOldThread(provider, world.SpaceB, world.UserB1, 2);
        await MigrateTo(scratch, null);

        (await Parents(provider, world.SpaceA, world.UserA1, inA.Thread)).Should().Equal(null, inA.Messages[0], inA.Messages[1]);
        (await Parents(provider, world.SpaceB, world.UserB1, inB.Thread)).Should().Equal(null, inB.Messages[0]);
        (await PolicyIsForced(scratch, "messages")).Should().BeTrue("the owner reads past the policies only inside the migration");
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Message Said(ChatThread thread, string text, int? position = null, DateTimeOffset? shelvedAt = null) => new()
    {
        ThreadId = thread.Id,
        AudienceUserId = thread.AudienceUserId,
        Position = position,
        Role = MessageRole.User,
        AuthorUserId = thread.OwnerUserId,
        Parts = [new TextPart(text)],
        ShelvedAt = shelvedAt,
    };

    private static async Task<ChatThread> NewThread(IServiceProvider provider, Guid spaceId, Guid owner)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var thread = CoreRows.NewThread(bot, owner, owner);
        thread.LastActivityAt = DateTimeOffset.UtcNow;
        session.Db.Threads.Add(thread);
        await session.Commit();
        return thread;
    }

    private static async Task Append(IServiceProvider provider, TenantWorld world, ChatThread thread, params Message[] messages)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        await new MessageRepository(session.Db).AppendMessages(thread, messages, Token);
        await session.Commit();
    }

    private static async Task Insert(IServiceProvider provider, Guid spaceId, Guid userId, ChatThread thread, Message message)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        message.ThreadId = thread.Id;
        session.Db.Messages.Add(message);
        await session.Commit();
    }

    private static async Task<List<string>> ShownTexts(IServiceProvider provider, TenantWorld world, ChatThread thread)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var shown = await new MessageRepository(session.Db).ListMessages(thread.Id, Token);
        return [.. shown.Select(message => message.Parts.OfType<TextPart>().Single().Text)];
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListVersions(IServiceProvider provider, TenantWorld world, ChatThread thread)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        return await new MessageRepository(session.Db).ListVersions(thread.Id, Token);
    }

    /// <summary>A thread and its messages written through the application role, before messages named their parent.</summary>
    private static async Task<(Guid Thread, List<Guid> Messages)> InsertOldThread(IServiceProvider provider, Guid spaceId, Guid owner, int count)
    {
        var id = Guid.CreateVersion7();
        List<Guid> messages = [];
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        await session.Db.SaveChangesAsync(Token);
        await session.Execute(
            """
            INSERT INTO threads (id, space_id, bot_id, owner_user_id, audience_user_id, origin, last_activity_at)
            VALUES (@id, @space, @bot, @owner, @owner, 'chat', now())
            """,
            ("id", id),
            ("space", spaceId),
            ("bot", bot.Id),
            ("owner", owner));

        for (var position = 1; position <= count; position++)
        {
            var messageId = Guid.CreateVersion7();
            messages.Add(messageId);
            await session.Execute(
                """
                INSERT INTO messages (id, space_id, thread_id, audience_user_id, position, role, author_user_id, parts)
                VALUES (@id, @space, @thread, @owner, @position, 'user', @owner, '[]'::jsonb)
                """,
                ("id", messageId),
                ("space", spaceId),
                ("thread", id),
                ("position", position),
                ("owner", owner));
        }

        await session.Commit();
        return (id, messages);
    }

    private static async Task<List<Guid?>> Parents(IServiceProvider provider, Guid spaceId, Guid userId, Guid threadId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return await session.Db.Messages.AsNoTracking()
            .Where(message => message.ThreadId == threadId)
            .OrderBy(message => message.Position)
            .Select(message => message.ParentId)
            .ToListAsync(Token);
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
