namespace Drover.Data.Tests;

/// <summary>
/// The notes a bot keeps about a person: private to that person, invisible to every other member and every other
/// space, never tied to a conversation the person cannot read, and always written by exactly one bot or one person.
/// The generated isolation suite covers the table as well; these tests read it the way the engine does.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class MemoryNoteTests(PostgresFixture postgres)
{
    #region Audience

    [Fact]
    public async Task A_note_private_to_one_person_is_invisible_to_another_member_and_to_another_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot);
        var note = await SaveNote(provider, world.SpaceA, world.UserA1, bot.Id, said.ThreadId, said.Id);

        var mine = await ListNotes(provider, world.SpaceA, world.UserA1, bot.Id, world.UserA1);
        var asked = await ListNotes(provider, world.SpaceA, world.UserA2, bot.Id, world.UserA1);
        var own = await ListNotes(provider, world.SpaceA, world.UserA2, bot.Id, world.UserA2);
        var elsewhere = await ListNotes(provider, world.SpaceB, world.UserB1, bot.Id, world.UserA1);
        await using var other = await TenantSession.Open(provider, world.SpaceA, world.UserA2);

        mine.Select(found => found.Id).Should().Equal(note.Id);
        asked.Should().BeEmpty("another member asking for the first person's notes by name still gets none");
        own.Should().BeEmpty("the bot keeps no notes about the second member");
        elsewhere.Should().BeEmpty("another space sees no note at all");
        (await new MemoryRepository(other.Db).GetNote(note.Id, bot.Id, world.UserA1, Token)).Should().BeNull();
        (await other.Db.Memories.CountAsync(Token)).Should().Be(0);
    }

    [Fact]
    public async Task Sharing_the_conversation_a_note_came_from_leaves_the_note_private_to_its_person()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot);
        var note = await SaveNote(provider, world.SpaceA, world.UserA1, bot.Id, said.ThreadId, said.Id);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await session.Db.Threads.Where(thread => thread.Id == said.ThreadId)
                .ExecuteUpdateAsync(set => set.SetProperty(thread => thread.AudienceUserId, Audience.Everyone), Token)).Should().Be(1);
            await session.Commit();
        }

        var mine = await ListNotes(provider, world.SpaceA, world.UserA1, bot.Id, world.UserA1);
        var theirs = await ListNotes(provider, world.SpaceA, world.UserA2, bot.Id, world.UserA1);

        mine.Should().ContainSingle().Which.Should().Match<MemoryNote>(kept =>
            kept.Id == note.Id && kept.AudienceUserId == world.UserA1 && kept.SourceMessageId == said.Id);
        theirs.Should().BeEmpty("a note never moves to everyone with the conversation it came from");
    }

    [Fact]
    public async Task Deleting_the_message_a_note_came_from_is_not_blocked_and_keeps_the_note()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot);
        var note = await SaveNote(provider, world.SpaceA, world.UserA1, bot.Id, said.ThreadId, said.Id);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await session.Db.Messages.Where(message => message.Id == said.Id).ExecuteDeleteAsync(Token)).Should().Be(1);
            await session.Commit();
        }

        (await ListNotes(provider, world.SpaceA, world.UserA1, bot.Id, world.UserA1)).Should().ContainSingle().Which
            .Should().Match<MemoryNote>(kept => kept.Id == note.Id
                && kept.AudienceUserId == world.UserA1 && kept.WrittenByBotId == bot.Id);
    }

    [Fact]
    public async Task A_note_is_written_by_a_bot_or_a_person_and_only_a_bots_note_names_a_conversation()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot);

        var byNobody = () => Save(provider, world, new MemoryNote { BotId = bot.Id, AudienceUserId = world.UserA1, Text = "Likes tea." });
        var byBoth = () => Save(provider, world, new MemoryNote
        {
            BotId = bot.Id,
            AudienceUserId = world.UserA1,
            Text = "Likes tea.",
            WrittenByBotId = bot.Id,
            WrittenByUserId = world.UserA1,
        });
        var personWithSource = () => Save(provider, world, new MemoryNote
        {
            BotId = bot.Id,
            AudienceUserId = world.UserA1,
            Text = "Likes tea.",
            WrittenByUserId = world.UserA1,
            SourceThreadId = said.ThreadId,
            SourceMessageId = said.Id,
        });
        var blank = () => Save(provider, world, new MemoryNote { BotId = bot.Id, AudienceUserId = world.UserA1, Text = "  ", WrittenByUserId = world.UserA1 });

        foreach (var refused in new[] { byNobody, byBoth, personWithSource, blank })
        {
            (await refused.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await Save(provider, world, new MemoryNote { BotId = bot.Id, AudienceUserId = world.UserA1, Text = "Likes tea.", WrittenByUserId = world.UserA1 });
        (await ListNotes(provider, world.SpaceA, world.UserA1, bot.Id, world.UserA1)).Should().ContainSingle();
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

    /// <summary>A private conversation of <paramref name="owner"/> with one message in it.</summary>
    private static async Task<Message> SaveThread(IServiceProvider provider, Guid spaceId, Guid owner, Bot bot)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        var thread = CoreRows.NewThread(bot, owner, owner);
        var message = CoreRows.NewMessage(thread, 1, "I like short answers.");
        session.Db.Threads.Add(thread);
        session.Db.Messages.Add(message);
        await session.Commit();
        return message;
    }

    private static async Task<MemoryNote> SaveNote(IServiceProvider provider, Guid spaceId, Guid userId, Guid botId, Guid threadId, Guid messageId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var note = new MemoryNote
        {
            BotId = botId,
            AudienceUserId = userId,
            Text = "Prefers short answers.",
            WrittenByBotId = botId,
            SourceThreadId = threadId,
            SourceMessageId = messageId,
        };
        new MemoryRepository(session.Db).Add(note);
        await session.Commit();
        return note;
    }

    private static async Task Save(IServiceProvider provider, TenantWorld world, MemoryNote note)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        new MemoryRepository(session.Db).Add(note);
        await session.Commit();
    }

    private static async Task<List<MemoryNote>> ListNotes(IServiceProvider provider, Guid spaceId, Guid userId, Guid botId, Guid about)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return await new MemoryRepository(session.Db).ListNotes(botId, about, Token);
    }

    #endregion
}
