namespace Drover.Data.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class TenantWorkTests(PostgresFixture postgres)
{
    #region Public Methods

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abandoned_changes_do_not_survive_into_the_next_scope(bool saveBeforeRollback)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(world.SpaceA, world.UserA1);
        var claim = new UserClaim { UserId = world.UserA1, ClaimType = "abandoned" };

        await using (var work = await session.Scope.Begin(TestContext.Current.CancellationToken))
        {
            var space = await session.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken);
            space.Name = "abandoned";
            session.Db.UserClaims.Add(claim);
            if (saveBeforeRollback)
            {
                await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        await using (var work = await session.Scope.Begin(TestContext.Current.CancellationToken))
        {
            var space = await session.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken);
            space.Name.Should().Be($"Space {world.SpaceA:N}");
            await work.Commit(TestContext.Current.CancellationToken);
        }

        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await check.Db.UserClaims.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await check.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be($"Space {world.SpaceA:N}");
    }

    [Fact]
    public async Task Code_can_tell_whether_it_is_inside_a_tenant_transaction()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(world.SpaceA, world.UserA1);
        TenantScope.IsOpenInCurrentFlow.Should().BeFalse();

        await using (var work = await session.Scope.Begin(TestContext.Current.CancellationToken))
        {
            TenantScope.IsOpenInCurrentFlow.Should().BeTrue();
            (await Task.Run(() => TenantScope.IsOpenInCurrentFlow)).Should().BeTrue("work started inside the transaction is inside it too");
            (await ReadsFlagAfterAnAwait()).Should().BeTrue();
            await work.Commit(TestContext.Current.CancellationToken);
            TenantScope.IsOpenInCurrentFlow.Should().BeTrue("a committed transaction is still open until its work is disposed");
        }

        TenantScope.IsOpenInCurrentFlow.Should().BeFalse("the transaction ended with its work");
        await OpensAndClosesAScope(session);
        TenantScope.IsOpenInCurrentFlow.Should().BeFalse("a scope a callee opened and closed leaves nothing behind");
    }

    [Fact]
    public async Task Failed_commit_does_not_poison_the_next_scope()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(world.SpaceA, world.UserA1);

        await using (var work = await session.Scope.Begin(TestContext.Current.CancellationToken))
        {
            (await session.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name = "abandoned";
            session.Db.UserClaims.Add(new() { UserId = world.UserA2, ClaimType = "forbidden" });
            var commit = () => work.Commit(TestContext.Current.CancellationToken);
            await commit.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var work = await session.Scope.Begin(TestContext.Current.CancellationToken))
        {
            (await session.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name = "committed";
            await work.Commit(TestContext.Current.CancellationToken);
        }

        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await check.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("committed");
    }

    [Fact]
    public async Task Successful_scopes_can_be_followed_by_another_successful_scope()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(world.SpaceA, world.UserA1);
        foreach (var name in new[] { "first", "second" })
        {
            await using var work = await session.Scope.Begin(TestContext.Current.CancellationToken);
            (await session.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name = name;
            await work.Commit(TestContext.Current.CancellationToken);
        }

        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await check.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("second");
    }

    #endregion

    #region Private Methods

    private static async Task<bool> ReadsFlagAfterAnAwait()
    {
        await Task.Yield();
        return TenantScope.IsOpenInCurrentFlow;
    }

    private static async Task OpensAndClosesAScope(TenantSession session)
    {
        await using var work = await session.Scope.Begin(TestContext.Current.CancellationToken);
        TenantScope.IsOpenInCurrentFlow.Should().BeTrue();
    }

    #endregion
}
