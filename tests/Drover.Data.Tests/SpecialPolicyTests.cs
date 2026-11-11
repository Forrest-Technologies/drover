namespace Drover.Data.Tests;

/// <summary>The policies of <c>spaces</c>, <c>users</c>, and <c>memberships</c>, each written out in the first migration.</summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class SpecialPolicyTests(PostgresFixture postgres)
{
    #region Users

    [Fact]
    public async Task A_user_sees_themselves_and_the_members_of_the_current_space_only()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        (await VisibleUsers(provider, world.SpaceA, world.UserA1))
            .Should().BeEquivalentTo([world.UserA1, world.UserA2, world.Traveler]);
        (await VisibleUsers(provider, world.SpaceB, world.UserB1))
            .Should().BeEquivalentTo([world.UserB1, world.Traveler]);
    }

    [Fact]
    public async Task With_no_space_chosen_a_user_sees_only_themselves()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        (await VisibleUsers(provider, spaceId: null, world.UserA1)).Should().Equal(world.UserA1);
    }

    [Fact]
    public async Task A_user_cannot_change_another_users_row()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var otherUpdated = await session.Db.Users
            .Where(user => user.Id == world.UserA2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.DisplayName, "taken over"), TestContext.Current.CancellationToken);
        var ownUpdated = await session.Db.Users
            .Where(user => user.Id == world.UserA1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.DisplayName, "renamed"), TestContext.Current.CancellationToken);

        otherUpdated.Should().Be(0);
        ownUpdated.Should().Be(1);
    }

    [Fact]
    public async Task A_user_cannot_create_a_row_for_someone_else()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var other = Guid.CreateVersion7();
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        session.Db.Users.Add(new User { Id = other, UserName = $"x-{other:N}", NormalizedUserName = $"X-{other:N}" });

        var save = () => session.Commit();

        var thrown = await save.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Nobody_can_delete_a_user_row()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var delete = () => session.Db.Users.Where(user => user.Id == world.UserA1).ExecuteDeleteAsync();

        var thrown = await delete.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    #endregion

    #region Memberships

    [Fact]
    public async Task Before_a_space_is_chosen_a_user_lists_their_own_memberships_in_every_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, spaceId: null, world.Traveler);

        var spaces = await session.Db.Memberships.Select(membership => membership.SpaceId).ToListAsync(TestContext.Current.CancellationToken);

        spaces.Should().BeEquivalentTo([world.SpaceA, world.SpaceB]);
    }

    [Fact]
    public async Task A_user_cannot_add_a_membership_in_a_space_they_are_not_working_in()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, spaceId: null, world.UserA1);
        session.Db.Memberships.Add(new Membership { SpaceId = world.SpaceB, UserId = world.UserA1, Role = MembershipRole.Owner });

        var save = () => session.Commit();

        var thrown = await save.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("42501");
    }

    #endregion

    #region Spaces

    [Fact]
    public async Task A_space_row_is_visible_and_writable_only_as_that_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var otherUpdated = await session.Db.Spaces
            .Where(space => space.Id == world.SpaceB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(space => space.Name, "taken over"), TestContext.Current.CancellationToken);
        var ownUpdated = await session.Db.Spaces
            .Where(space => space.Id == world.SpaceA)
            .ExecuteUpdateAsync(setters => setters.SetProperty(space => space.Name, "renamed"), TestContext.Current.CancellationToken);

        otherUpdated.Should().Be(0);
        ownUpdated.Should().Be(1);
    }

    [Fact]
    public async Task Space_settings_are_stored_as_snake_case_json()
    {
        await using var provider = postgres.BuildServices();
        var spaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await using (var session = await TenantSession.Open(provider, spaceId, userId))
        {
            session.Db.Users.Add(new User { Id = userId, UserName = $"s-{userId:N}", NormalizedUserName = $"S-{userId:N}" });
            session.Db.Spaces.Add(new Space
            {
                Id = spaceId,
                Kind = SpaceKind.Personal,
                Name = "Personal",
                Slug = $"personal-{spaceId:N}",
                Settings = new SpaceSettings { MaxBotDepth = 3 },
            });
            await session.Commit();
        }

        await using var check = await TenantSession.Open(provider, spaceId, userId);
        var raw = await check.Db.Database
            .SqlQueryRaw<string>("SELECT settings::text AS \"Value\" FROM spaces")
            .SingleAsync(TestContext.Current.CancellationToken);
        var space = await check.Db.Spaces.SingleAsync(TestContext.Current.CancellationToken);

        raw.Should().Contain("\"max_bot_depth\": 3");
        space.Settings.MaxBotDepth.Should().Be(3);
        space.Kind.Should().Be(SpaceKind.Personal);
    }

    #endregion

    #region Identity Tables

    [Fact]
    public async Task Identity_child_tables_belong_to_their_user()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Db.Database.ExecuteSqlAsync(
                $"INSERT INTO user_claims (user_id, claim_type, claim_value) VALUES ({world.UserA1}, 'k', 'v')", TestContext.Current.CancellationToken);
            await session.Commit();
        }

        await using var other = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
        var seen = await other.Db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM user_claims").SingleAsync(TestContext.Current.CancellationToken);
        var insert = () => other.Db.Database.ExecuteSqlAsync(
            $"INSERT INTO user_claims (user_id, claim_type, claim_value) VALUES ({world.UserA1}, 'k', 'v')");

        seen.Should().Be(0);
        var thrown = await insert.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    #endregion

    #region Helpers

    private static async Task<List<Guid>> VisibleUsers(ServiceProvider provider, Guid? spaceId, Guid userId)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        return await session.Db.Users.Select(user => user.Id).ToListAsync();
    }

    #endregion
}
