namespace Drover.Data.Tests;

/// <summary>
/// Sandbox rows: one live row per owner bot, enforced by the database so two first uses at once make one sandbox,
/// and the bot's path that decides which sandbox it works in.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class SandboxRepositoryTests(PostgresFixture postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_second_live_sandbox_for_the_same_owner_is_refused_and_recognized_as_such()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        await AddSandbox(provider, world, bot.Id, SandboxState.Creating);

        var second = () => AddSandbox(provider, world, bot.Id, SandboxState.Running);

        var thrown = await second.Should().ThrowAsync<DbUpdateException>();
        SandboxRepository.IsAlreadyLive(thrown.Which).Should().BeTrue();
    }

    [Fact]
    public async Task A_retired_or_destroyed_sandbox_leaves_room_for_a_new_live_one_and_is_not_found_as_live()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        await AddSandbox(provider, world, bot.Id, SandboxState.Destroyed);
        await AddSandbox(provider, world, bot.Id, SandboxState.Retiring);
        var live = await AddSandbox(provider, world, bot.Id, SandboxState.Paused);

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var found = await new SandboxRepository(session.Db).FindLiveForOwner(bot.Id, TestContext.Current.CancellationToken);

        found!.Id.Should().Be(live);
    }

    [Fact]
    public async Task The_same_owner_id_in_another_space_does_not_count_and_is_not_seen()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        await AddSandbox(provider, world, bot.Id, SandboxState.Running);

        await using var session = await TenantSession.Open(provider, world.SpaceB, world.UserB1);
        var seen = await new SandboxRepository(session.Db).FindLiveForOwner(bot.Id, TestContext.Current.CancellationToken);

        seen.Should().BeNull();
    }

    [Fact]
    public async Task A_bots_sandbox_path_runs_from_itself_up_and_leaves_out_removed_bots()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        session.Db.ModelConnections.Add(connection);
        var top = CoreRows.NewBot(connection);
        top.SandboxPolicy = SandboxPolicy.Subtree;
        var middle = CoreRows.NewBot(connection, top);
        middle.Status = BotStatus.Removed;
        var bottom = CoreRows.NewBot(connection, middle);
        bottom.SandboxPolicy = SandboxPolicy.Private;
        CoreRows.AddBot(session.Db, top, []);
        CoreRows.AddBot(session.Db, middle, [top]);
        CoreRows.AddBot(session.Db, bottom, [middle, top]);
        await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var path = await new BotRepository(session.Db).ListSandboxPath(bottom.Id, TestContext.Current.CancellationToken);

        path.Should().Equal(new SandboxPathEntry(bottom.Id, 0, SandboxPolicy.Private), new SandboxPathEntry(top.Id, 2, SandboxPolicy.Subtree));
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

    private static async Task<Guid> AddSandbox(IServiceProvider provider, TenantWorld world, Guid ownerBotId, SandboxState state)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var sandbox = new Sandbox { OwnerBotId = ownerBotId, Provider = "test", ProviderSandboxId = $"sbx{Guid.NewGuid():N}", State = state };
        new SandboxRepository(session.Db).Add(sandbox);
        await session.Commit();
        return sandbox.Id;
    }

    #endregion
}
