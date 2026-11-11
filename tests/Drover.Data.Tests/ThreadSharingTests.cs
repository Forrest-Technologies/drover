namespace Drover.Data.Tests;

/// <summary>
/// A thread is private to its owner or shared with the whole space. Sharing one statement carries to everything
/// beneath it, and no other member can take a thread over.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ThreadSharingTests(PostgresFixture postgres)
{
    #region Sharing

    [Fact]
    public async Task A_thread_shared_by_its_owner_becomes_visible_to_another_member_with_all_its_messages()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: false, messages: 3);

        (await Visible(provider, world, world.UserA2, thread)).Should().Be(new Visibility(Threads: 0, Messages: 0, Sessions: 0));
        (await Visible(provider, world, world.UserA1, thread)).Should().Be(new Visibility(Threads: 1, Messages: 3, Sessions: 1));

        await SetAudience(provider, world, world.UserA1, thread, Audience.Everyone);

        (await Visible(provider, world, world.UserA2, thread)).Should().Be(new Visibility(Threads: 1, Messages: 3, Sessions: 1));
        (await Visible(provider, world, world.Traveler, thread)).Should().Be(new Visibility(Threads: 1, Messages: 3, Sessions: 1));
    }

    [Fact]
    public async Task A_shared_thread_can_be_made_private_to_its_owner_again()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: true, messages: 2);
        (await Visible(provider, world, world.UserA2, thread)).Messages.Should().Be(2);

        await SetAudience(provider, world, world.UserA1, thread, world.UserA1);

        (await Visible(provider, world, world.UserA2, thread)).Should().Be(new Visibility(Threads: 0, Messages: 0, Sessions: 0));
        (await Visible(provider, world, world.UserA1, thread)).Messages.Should().Be(2);
    }

    #endregion

    #region Taking Over

    [Fact]
    public async Task A_member_cannot_make_someone_elses_shared_thread_private_to_themself()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: true, messages: 1);

        var toThemself = () => SetAudience(provider, world, world.UserA2, thread, world.UserA2);

        var thrown = await toThemself.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation, "the audience may only be the owner or everyone");
        await ShouldStillBeSharedAndOwnedByTheOwner(provider, world, thread);
    }

    [Fact]
    public async Task A_member_cannot_hide_someone_elses_shared_thread_from_everyone_but_its_owner()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: true, messages: 1);

        var toTheOwner = () => SetAudience(provider, world, world.UserA2, thread, world.UserA1);

        var thrown = await toTheOwner.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await ShouldStillBeSharedAndOwnedByTheOwner(provider, world, thread);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nobody_can_change_the_owner_of_a_thread_not_even_the_owner(bool ownerActs)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: true, messages: 0);
        var actor = ownerActs ? world.UserA1 : world.UserA2;
        await using var session = await TenantSession.Open(provider, world.SpaceA, actor);

        var takeOver = () => session.Execute(
            "UPDATE threads SET owner_user_id = @owner, audience_user_id = @owner WHERE id = @thread",
            ("owner", world.UserA2),
            ("thread", thread.Id));

        var thrown = await takeOver.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await ShouldStillBeSharedAndOwnedByTheOwner(provider, world, thread);
    }

    [Fact]
    public async Task A_member_cannot_create_a_thread_private_to_themself_that_names_someone_else_as_owner()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA2);

        var create = () => session.Execute(
            "INSERT INTO threads (id, space_id, audience_user_id, bot_id, owner_user_id, origin) VALUES (@id, @space, @audience, @bot, @owner, 'chat')",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceA),
            ("audience", world.UserA2),
            ("bot", bot.Id),
            ("owner", world.UserA1));

        var thrown = await create.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_message_cannot_be_added_to_a_shared_thread_with_a_narrower_audience()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var thread = await SaveThread(provider, world, shared: true, messages: 1);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA2);

        var hidden = () => session.Execute(
            "INSERT INTO messages (id, space_id, audience_user_id, thread_id, position, role) VALUES (@id, @space, @audience, @thread, 9, 'user')",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceA),
            ("audience", world.UserA2),
            ("thread", thread.Id));

        var thrown = await hidden.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    #endregion

    #region Private Methods

    private static async Task<Bot> SaveBot(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        await session.Commit();
        return bot;
    }

    private static async Task<ChatThread> SaveThread(IServiceProvider provider, TenantWorld world, bool shared, int messages)
    {
        var bot = await SaveBot(provider, world);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var thread = CoreRows.NewThread(bot, world.UserA1, shared ? Audience.Everyone : world.UserA1);
        session.Db.Threads.Add(thread);
        session.Db.Messages.AddRange(Enumerable.Range(1, messages).Select(position => CoreRows.NewMessage(thread, position, $"message {position}")));
        session.Db.AgentSessions.Add(new AgentSession
        {
            ThreadId = thread.Id,
            BotId = bot.Id,
            AudienceUserId = thread.AudienceUserId,
            State = CoreRows.EmptyJson(),
            FrameworkVersion = "test",
        });
        await session.Commit();
        return thread;
    }

    /// <summary>One statement, as the sharing service will write it. The audience is part of a key, so the change is not a tracked save.</summary>
    private static async Task SetAudience(IServiceProvider provider, TenantWorld world, Guid actor, ChatThread thread, Guid audience)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, actor);
        await session.Execute(
            "UPDATE threads SET audience_user_id = @audience WHERE id = @thread",
            ("audience", audience),
            ("thread", thread.Id));
        await session.Commit();
    }

    private static async Task<Visibility> Visible(IServiceProvider provider, TenantWorld world, Guid viewer, ChatThread thread)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, viewer);
        return new Visibility(
            await session.Db.Threads.CountAsync(found => found.Id == thread.Id, TestContext.Current.CancellationToken),
            await session.Db.Messages.CountAsync(found => found.ThreadId == thread.Id, TestContext.Current.CancellationToken),
            await session.Db.AgentSessions.CountAsync(found => found.ThreadId == thread.Id, TestContext.Current.CancellationToken));
    }

    private static async Task ShouldStillBeSharedAndOwnedByTheOwner(IServiceProvider provider, TenantWorld world, ChatThread thread)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await session.Db.Threads.SingleAsync(found => found.Id == thread.Id, TestContext.Current.CancellationToken);

        stored.OwnerUserId.Should().Be(world.UserA1);
        stored.AudienceUserId.Should().Be(Audience.Everyone);
    }

    #endregion

    #region Nested Types

    private sealed record Visibility(int Threads, int Messages, int Sessions);

    #endregion
}
