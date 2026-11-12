namespace Drover.Data.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class TenantIsolationTests(PostgresFixture postgres)
{
    #region Pool Of One

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_a_pool_of_one_each_space_sees_only_its_own_rows(bool noResetOnClose)
    {
        await using var seedServices = postgres.BuildServices();
        var world = await TenantWorld.Seed(seedServices);
        await using var provider = postgres.BuildServices(maxPoolSize: 1, noResetOnClose);

        var seenByA = await Observe(provider, world.SpaceA, world.UserA1);
        var seenByB = await Observe(provider, world.SpaceB, world.UserB1);
        var seenByNobody = await Observe(provider, spaceId: null, userId: null);
        var seenByAAgain = await Observe(provider, world.SpaceA, world.UserA1);

        seenByA.Spaces.Should().Equal(world.SpaceA);
        seenByA.MembershipSpaces.Should().OnlyContain(space => space == world.SpaceA).And.HaveCount(3);
        seenByB.Spaces.Should().Equal(world.SpaceB);
        seenByB.MembershipSpaces.Should().OnlyContain(space => space == world.SpaceB).And.HaveCount(2);
        seenByNobody.Spaces.Should().BeEmpty();
        seenByNobody.MembershipSpaces.Should().BeEmpty();
        seenByNobody.UserCount.Should().Be(0);
        seenByAAgain.Should().BeEquivalentTo(seenByA, options => options.Excluding(seen => seen.BackendPid));
        new[] { seenByA, seenByB, seenByNobody, seenByAAgain }
            .Select(seen => seen.BackendPid)
            .Distinct()
            .Should()
            .ContainSingle("a pool of one hands every scope the same physical connection");
    }

    [Fact]
    public async Task A_rolled_back_or_faulted_transaction_leaves_nothing_for_the_next_tenant()
    {
        await using var seedServices = postgres.BuildServices();
        var world = await TenantWorld.Seed(seedServices);
        await using var provider = postgres.BuildServices(maxPoolSize: 1, noResetOnClose: true);

        for (var round = 0; round < 25; round++)
        {
            await Fault(provider, world.SpaceA, world.UserA1);
            (await Observe(provider, world.SpaceB, world.UserB1)).Spaces.Should().Equal(world.SpaceB);
            (await Observe(provider, spaceId: null, userId: null)).Spaces.Should().BeEmpty();
            (await Observe(provider, world.SpaceA, world.UserA1)).Spaces.Should().Equal(world.SpaceA);
        }
    }

    #endregion

    #region Guard

    [Fact]
    public async Task A_query_outside_a_tenant_scope_throws_and_names_TenantScope()
    {
        await using var provider = postgres.BuildServices();
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(Guid.CreateVersion7(), Guid.CreateVersion7());

        var query = () => session.Db.Spaces.ToListAsync();

        var thrown = await query.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("TenantScope");
    }

    [Fact]
    public async Task Saving_outside_a_tenant_scope_throws_and_names_TenantScope()
    {
        await using var provider = postgres.BuildServices();
        await using var session = TenantSession.Create(provider);
        session.Tenant.Set(Guid.CreateVersion7(), Guid.CreateVersion7());
        session.Db.Spaces.Add(new Space { Kind = SpaceKind.Shared, Name = "Outside", Slug = $"outside-{Guid.NewGuid():N}" });

        var save = () => session.Db.SaveChangesAsync();

        var thrown = await save.Should().ThrowAsync<Exception>();
        thrown.Which.ToString().Should().Contain("TenantScope");
    }

    [Fact]
    public async Task Scopes_do_not_nest()
    {
        await using var provider = postgres.BuildServices();
        await using var session = await TenantSession.Open(provider, Guid.CreateVersion7(), Guid.CreateVersion7());

        var second = () => session.Scope.Begin();

        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TenantScope*");
    }

    #endregion

    #region Writes

    [Fact]
    public async Task Inserting_a_row_with_another_spaces_id_fails_with_42501()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        session.Db.Memberships.Add(new Membership { SpaceId = world.SpaceB, UserId = world.UserA1, Role = MembershipRole.Member });

        var save = () => session.Commit();

        var thrown = await save.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Moving_a_row_into_another_space_fails_with_42501()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var move = () => session.Db.Memberships
            .Where(membership => membership.UserId == world.UserA2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(membership => membership.SpaceId, world.SpaceB));

        var thrown = await move.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task A_new_row_gets_the_space_of_the_tenant()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var newcomer = Guid.CreateVersion7();

        await using (var session = await TenantSession.Open(provider, world.SpaceB, newcomer))
        {
            session.Db.Users.Add(new User { Id = newcomer, UserName = $"n-{newcomer:N}", NormalizedUserName = $"N-{newcomer:N}" });
            session.Db.Memberships.Add(new Membership { UserId = newcomer, Role = MembershipRole.Member });
            await session.Commit();
        }

        await using var check = await TenantSession.Open(provider, world.SpaceB, newcomer);
        var membership = await check.Db.Memberships.SingleAsync(found => found.UserId == newcomer, TestContext.Current.CancellationToken);
        membership.SpaceId.Should().Be(world.SpaceB);
    }

    [Fact]
    public async Task Disposing_a_scope_without_committing_rolls_back()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Db.Memberships
                .Where(membership => membership.UserId == world.UserA2)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await check.Db.Memberships.CountAsync(TestContext.Current.CancellationToken)).Should().Be(3);
    }

    #endregion

    #region Configuration

    [Fact]
    public async Task The_retrying_execution_strategy_is_off()
    {
        await using var provider = postgres.BuildServices();
        await using var session = TenantSession.Create(provider);

        session.Db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
    }

    [Fact]
    public async Task A_request_that_touches_no_tenant_data_opens_no_connection_and_no_transaction()
    {
        var applicationName = $"drover-idle-{Guid.NewGuid():N}";
        await using var provider = postgres.BuildServices(applicationName: applicationName);

        await using (var session = TenantSession.Create(provider))
        {
            session.Tenant.Set(Guid.CreateVersion7(), Guid.CreateVersion7());
            session.Db.Database.CurrentTransaction.Should().BeNull();
        }

        (await CountBackends(applicationName)).Should().Be(0, "nothing connected on behalf of the request");
    }

    [Fact]
    public async Task A_finished_scope_leaves_no_transaction_open_on_the_connection()
    {
        var applicationName = $"drover-finished-{Guid.NewGuid():N}";
        await using var provider = postgres.BuildServices(applicationName: applicationName);
        var world = await TenantWorld.Seed(provider);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Db.Spaces.ToListAsync(TestContext.Current.CancellationToken);
            await session.Commit();
        }

        (await CountBackends(applicationName, state: "idle in transaction")).Should().Be(0);
    }

    #endregion

    #region Helpers

    private async Task<Observation> Observe(ServiceProvider provider, Guid? spaceId, Guid? userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return new Observation(
            await session.BackendPid(),
            await session.Db.Spaces.Select(space => space.Id).ToListAsync(),
            await session.Db.Memberships.Select(membership => membership.SpaceId).ToListAsync(),
            await session.Db.Users.CountAsync());
    }

    private static async Task Fault(ServiceProvider provider, Guid spaceId, Guid userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        await session.Db.Spaces.ToListAsync();

        var fault = () => session.Db.Database.ExecuteSqlRawAsync("SELECT 1 / 0");

        await fault.Should().ThrowAsync<PostgresException>();
    }

    private async Task<long> CountBackends(string applicationName, string? state = null)
    {
        await using var connection = new NpgsqlConnection(postgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name AND (@state IS NULL OR state = @state)",
            connection);
        command.Parameters.AddWithValue("name", applicationName);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Observation(
        int BackendPid,
        List<Guid> Spaces,
        List<Guid> MembershipSpaces,
        int UserCount);

    #endregion
}
