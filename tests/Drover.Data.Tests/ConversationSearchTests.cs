namespace Drover.Data.Tests;

/// <summary>
/// Searching conversations reads through the same policies as every other query: a person finds words only in
/// their own space, only in the threads whose audience includes them (their own and the ones shared with everyone,
/// or only their own with Mine only), only in bots that are not removed, and only in what a person or a bot said.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ConversationSearchTests(PostgresFixture postgres)
{
    #region Isolation

    [Fact]
    public async Task Another_spaces_words_never_appear_even_for_a_member_of_both_spaces()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var botA = await SaveBot(provider, world.SpaceA, world.UserA1);
        var botB = await SaveBot(provider, world.SpaceB, world.UserB1);
        var inA = await SaveThread(provider, world.SpaceA, world.Traveler, botA, world.Traveler, "The quokka migration is planned for spring.");
        var inB = await SaveThread(provider, world.SpaceB, world.Traveler, botB, world.Traveler, "The quokka budget belongs to space B alone.");

        var travelerInA = await Search(provider, world.SpaceA, world.Traveler, "quokka");
        var travelerInB = await Search(provider, world.SpaceB, world.Traveler, "quokka");
        var ownerOfA = await Search(provider, world.SpaceA, world.UserA1, "budget");
        var ownerOfB = await Search(provider, world.SpaceB, world.UserB1, "migration");

        travelerInA.Select(hit => hit.MessageId).Should().Equal(inA.Id);
        travelerInA.Should().NotContain(hit => hit.Snippet.Contains("budget"), "space B's words never reach space A");
        travelerInB.Select(hit => hit.MessageId).Should().Equal(inB.Id);
        ownerOfA.Should().BeEmpty("the only message with that word is in space B");
        ownerOfB.Should().BeEmpty("the only message with that word is in space A");
    }

    [Fact]
    public async Task Another_spaces_conversations_never_appear_in_either_mode()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var botB = await SaveBot(provider, world.SpaceB, world.UserB1);
        await SaveThread(provider, world.SpaceB, world.UserB1, botB, Audience.Everyone, "The marmot ledger belongs to space B.");

        var travelerInA = await Search(provider, world.SpaceA, world.Traveler, "marmot");
        var travelerInAMine = await Search(provider, world.SpaceA, world.Traveler, "marmot", mineOnly: true);
        var ownerOfAShared = await Search(provider, world.SpaceA, world.UserA1, "marmot");

        travelerInA.Should().BeEmpty("a shared conversation of another space is not in this space");
        travelerInAMine.Should().BeEmpty();
        ownerOfAShared.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conversation_another_member_shared_is_found_by_everyone_in_the_space_and_Mine_only_hides_it()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot, world.UserA1, "Remember the wombat invoice.", main: true);

        var owner = await Search(provider, world.SpaceA, world.UserA1, "wombat");
        var otherBefore = await Search(provider, world.SpaceA, world.UserA2, "wombat");
        await Share(provider, world.SpaceA, world.UserA1, said.ThreadId);
        var otherAfter = await Search(provider, world.SpaceA, world.UserA2, "wombat");
        var otherMine = await Search(provider, world.SpaceA, world.UserA2, "wombat", mineOnly: true);
        var ownerMine = await Search(provider, world.SpaceA, world.UserA1, "wombat", mineOnly: true);
        var ownerAfter = await Search(provider, world.SpaceA, world.UserA1, "wombat");

        owner.Should().ContainSingle().Which.Should().Match<MessageMatch>(hit =>
            hit.MessageId == said.Id && hit.ThreadId == said.ThreadId && hit.BotId == bot.Id && hit.InMainConversation);
        otherBefore.Should().BeEmpty("another member of the space may not read a private conversation");
        otherAfter.Should().ContainSingle().Which.Should().Match<MessageMatch>(
            hit => hit.MessageId == said.Id && !hit.InMainConversation,
            "a shared conversation is found by another member, and it is not that member's main conversation");
        otherMine.Should().BeEmpty("Mine only hides what someone else shared");
        ownerMine.Should().ContainSingle("Mine only keeps the person's own conversation");
        ownerAfter.Should().ContainSingle().Which.InMainConversation.Should().BeTrue("sharing does not change whose main conversation it is");
    }

    [Fact]
    public async Task Another_members_private_conversation_never_appears_in_either_mode()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        await SaveThread(provider, world.SpaceA, world.UserA2, bot, world.UserA2, "The ocelot password reset is private.");

        var everything = await Search(provider, world.SpaceA, world.UserA1, "ocelot");
        var mine = await Search(provider, world.SpaceA, world.UserA1, "ocelot", mineOnly: true);
        var theirs = await Search(provider, world.SpaceA, world.UserA2, "ocelot");

        everything.Should().BeEmpty("the audience is one other person");
        mine.Should().BeEmpty();
        theirs.Should().ContainSingle();
    }

    [Fact]
    public async Task A_removed_bots_conversation_is_not_searched_in_either_mode_and_does_not_use_up_the_window()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var kept = await SaveBot(provider, world.SpaceA, world.UserA1);
        var gone = await SaveBot(provider, world.SpaceA, world.UserA1);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var visible = await SaveThread(provider, world.SpaceA, world.UserA1, kept, world.UserA1, "The lynx report is ready.");
        var sharedByGone = await SaveThread(provider, world.SpaceA, world.UserA2, gone, Audience.Everyone, "The lynx archive is shared.");
        var ownedByGone = await SaveThread(provider, world.SpaceA, world.UserA1, gone, world.UserA1, "The lynx notes are mine.");
        await Stamp(provider, world.SpaceA, world.UserA1, visible.Id, start);
        await Stamp(provider, world.SpaceA, world.UserA1, ownedByGone.Id, start.AddMinutes(1));
        await Stamp(provider, world.SpaceA, world.UserA2, sharedByGone.Id, start.AddMinutes(2));
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Db.Bots.Where(bot => bot.Id == gone.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(bot => bot.Status, BotStatus.Removed), Token);
            await session.Commit();
        }

        var everything = await Search(provider, world.SpaceA, world.UserA1, "lynx");
        var mine = await Search(provider, world.SpaceA, world.UserA1, "lynx", mineOnly: true);
        var narrow = await SearchWindow(provider, world.SpaceA, world.UserA1, "lynx", null, candidateLimit: 1);

        everything.Select(hit => hit.MessageId).Should().Equal(visible.Id);
        mine.Select(hit => hit.MessageId).Should().Equal(visible.Id);
        narrow.Matches.Select(hit => hit.MessageId).Should().Equal(
            [visible.Id],
            "messages of a removed bot are not in the window, so they cannot push a visible one out of it");
        narrow.Older.Should().BeNull("nothing searchable is left past the window");
    }

    #endregion

    #region What Is Searched

    [Fact]
    public async Task Only_the_words_a_person_or_a_bot_said_are_searched()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot, world.UserA1, "Deploying the servers tonight.");
        var reply = new Message
        {
            ThreadId = said.ThreadId,
            AudienceUserId = world.UserA1,
            Position = 2,
            Role = MessageRole.Assistant,
            AuthorBotId = bot.Id,
            Parts =
            [
                new ReasoningPart("Thinking about the pangolin."),
                new ToolCallPart(Guid.CreateVersion7(), "call-1", "notes", JsonElement.Parse("""{"topic":"pangolin"}""")),
                new TextPart("I will deploy them after the backup."),
            ],
        };
        var pending = CoreRows.NewMessage(new ChatThread { Id = said.ThreadId, AudienceUserId = world.UserA1, OwnerUserId = world.UserA1 }, 0, "An unsent pangolin.");
        pending.Position = null;
        var shelved = CoreRows.NewMessage(new ChatThread { Id = said.ThreadId, AudienceUserId = world.UserA1, OwnerUserId = world.UserA1 }, 3, "A shelved pangolin reply.");
        shelved.ShelvedAt = DateTimeOffset.UtcNow;
        await Save(provider, world.SpaceA, world.UserA1, reply, pending, shelved);

        var deploy = await Search(provider, world.SpaceA, world.UserA1, "deploy");
        var hidden = await Search(provider, world.SpaceA, world.UserA1, "pangolin");

        deploy.Select(hit => (hit.MessageId, hit.FromBot)).Should().BeEquivalentTo([(said.Id, false), (reply.Id, true)], "stemming matches deploying and deploy");
        hidden.Should().BeEmpty("reasoning, tool arguments, pending messages, and shelved versions are not what is currently shown");
    }

    [Fact]
    public async Task An_archived_thread_is_not_searched()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var said = await SaveThread(provider, world.SpaceA, world.UserA1, bot, world.UserA1, "The axolotl tank needs cleaning.");

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Db.Threads.Where(thread => thread.Id == said.ThreadId)
                .ExecuteUpdateAsync(set => set.SetProperty(thread => thread.ArchivedAt, DateTimeOffset.UtcNow), Token);
            await session.Commit();
        }

        (await Search(provider, world.SpaceA, world.UserA1, "axolotl")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_snippet_marks_each_matched_word()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        await SaveThread(provider, world.SpaceA, world.UserA1, bot, world.UserA1, "Order more \u0001capybara\u0002 food before the capybara wakes.");

        var found = await Search(provider, world.SpaceA, world.UserA1, "capybara");

        found.Should().ContainSingle().Which.Snippet.Should().Be(
            $"Order more {MessageRepository.MatchStart}capybara{MessageRepository.MatchEnd} food before the {MessageRepository.MatchStart}capybara{MessageRepository.MatchEnd} wakes",
            "marks already in the text are taken out before the matches are marked");
    }

    [Fact]
    public async Task Only_the_most_recent_candidates_are_scanned()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var start = DateTimeOffset.UtcNow.AddHours(-2);

        Guid threadId;
        Message oldMatch;
        Message recentMatch;
        Message windowEdge;
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            threadId = thread.Id;
            session.Db.Threads.Add(thread);

            oldMatch = CoreRows.NewMessage(thread, 1, "Ancient nautilus fossil.");
            oldMatch.CreatedAt = start;
            session.Db.Messages.Add(oldMatch);

            var position = 2;
            List<Message> bulk = [];
            for (var index = 1; index < MessageRepository.SearchCandidateLimit; index++)
            {
                var filler = CoreRows.NewMessage(thread, position++, "Routine log entry.");
                filler.CreatedAt = start.AddSeconds(index);
                bulk.Add(filler);
            }

            windowEdge = bulk[0];
            recentMatch = CoreRows.NewMessage(thread, position, "Fresh nautilus sighting.");
            recentMatch.CreatedAt = start.AddSeconds(MessageRepository.SearchCandidateLimit);
            bulk.Add(recentMatch);

            session.Db.Messages.AddRange(bulk);
            await session.Commit();
        }

        var newest = await SearchWindow(provider, world.SpaceA, world.UserA1, "nautilus", null);

        newest.Matches.Select(hit => hit.MessageId).Should().Equal(
            [recentMatch.Id],
            "the older message sits past the recent candidate window");
        // PostgreSQL keeps microseconds and a .NET time keeps 100 nanosecond ticks, so the stored time is compared.
        newest.Older.Should().Be(
            new MessageKey(Microseconds(windowEdge.CreatedAt), windowEdge.Id),
            "the search says where its window ended, because an older message was not looked at");

        var older = await SearchWindow(provider, world.SpaceA, world.UserA1, "nautilus", newest.Older);

        older.Matches.Select(hit => hit.MessageId).Should().Equal(
            [oldMatch.Id],
            "searching on from where the window ended finds the message that was left out");
        older.Older.Should().BeNull("every readable message has now been looked at");
    }

    [Fact]
    public async Task Paging_through_small_windows_covers_every_message_once_and_counts_only_searchable_ones()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);
        var start = DateTimeOffset.UtcNow.AddHours(-3);

        List<Message> wanted = [];
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            var archived = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            archived.ArchivedAt = DateTimeOffset.UtcNow;
            session.Db.Threads.AddRange(thread, archived);

            for (var index = 0; index < 5; index++)
            {
                var message = CoreRows.NewMessage(thread, index + 1, $"Nautilus sighting number {index}.");
                message.CreatedAt = start.AddMinutes(index * 2);
                wanted.Add(message);

                // An archived thread's message sits between each pair. It must not be searched or counted in a window.
                var hidden = CoreRows.NewMessage(archived, index + 1, $"Nautilus hidden number {index}.");
                hidden.CreatedAt = start.AddMinutes((index * 2) + 1);
                session.Db.Messages.Add(hidden);
            }

            session.Db.Messages.AddRange(wanted);
            await session.Commit();
        }

        List<Guid> seen = [];
        MessageKey? cursor = null;
        List<int> pageSizes = [];
        do
        {
            var page = await SearchWindow(provider, world.SpaceA, world.UserA1, "nautilus", cursor, candidateLimit: 2);
            pageSizes.Add(page.Matches.Count);
            seen.AddRange(page.Matches.Select(hit => hit.MessageId));
            cursor = page.Older;
        }
        while (cursor is not null && pageSizes.Count < 10);

        pageSizes.Should().Equal(2, 2, 1);
        seen.Should().BeEquivalentTo(wanted.Select(message => message.Id), "no message is skipped and none is searched twice");
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_history_that_fits_the_window_is_searched_whole()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world.SpaceA, world.UserA1);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            session.Db.Threads.Add(thread);
            session.Db.Messages.Add(CoreRows.NewMessage(thread, 1, "A single nautilus."));
            await session.Commit();
        }

        var result = await SearchWindow(provider, world.SpaceA, world.UserA1, "nautilus", null);

        result.Matches.Should().ContainSingle();
        result.Older.Should().BeNull();
    }

    [Fact]
    public async Task A_search_outside_a_transaction_is_refused_so_the_time_limit_always_applies()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = TenantSession.Create(provider);
        session.Tenant.SetUser(world.UserA1);
        session.Tenant.SetSpace(world.SpaceA);

        var act = () => new MessageRepository(session.Db).SearchMessages("nautilus", 30, null, false, Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Helpers

    private static DateTimeOffset Microseconds(DateTimeOffset time) =>
        new(time.Ticks - time.Ticks % (TimeSpan.TicksPerMillisecond / 1000), time.Offset);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<MessageMatch>> Search(IServiceProvider provider, Guid spaceId, Guid userId, string words, bool mineOnly = false) =>
        (await SearchWindow(provider, spaceId, userId, words, null, mineOnly: mineOnly)).Matches;

    private static async Task Stamp(IServiceProvider provider, Guid spaceId, Guid userId, Guid messageId, DateTimeOffset at)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        await session.Db.Messages.Where(message => message.Id == messageId)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.CreatedAt, at), Token);
        await session.Commit();
    }

    private static async Task<MessageSearch> SearchWindow(
        IServiceProvider provider,
        Guid spaceId,
        Guid userId,
        string words,
        MessageKey? before,
        int candidateLimit = MessageRepository.SearchCandidateLimit,
        bool mineOnly = false)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return await new MessageRepository(session.Db).SearchMessages(words, 30, before, mineOnly, Token, candidateLimit);
    }

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

    /// <summary>A conversation of <paramref name="owner"/> with one message from them in it.</summary>
    private static async Task<Message> SaveThread(
        IServiceProvider provider,
        Guid spaceId,
        Guid owner,
        Bot bot,
        Guid audience,
        string text,
        bool main = false)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        var thread = CoreRows.NewThread(bot, owner, audience);
        thread.IsMain = main;
        var message = CoreRows.NewMessage(thread, 1, text);
        session.Db.Threads.Add(thread);
        session.Db.Messages.Add(message);
        await session.Commit();
        return message;
    }

    private static async Task Save(IServiceProvider provider, Guid spaceId, Guid userId, params Message[] messages)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        session.Db.Messages.AddRange(messages);
        await session.Commit();
    }

    private static async Task Share(IServiceProvider provider, Guid spaceId, Guid owner, Guid threadId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, owner);
        (await session.Db.Threads.Where(thread => thread.Id == threadId)
            .ExecuteUpdateAsync(set => set.SetProperty(thread => thread.AudienceUserId, Audience.Everyone), Token)).Should().Be(1);
        await session.Commit();
    }

    #endregion
}
