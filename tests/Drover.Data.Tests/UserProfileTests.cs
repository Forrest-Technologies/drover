namespace Drover.Data.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserProfileTests(PostgresFixture postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_user_cannot_delete_their_required_identity_profile()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var delete = () => session.Db.UserProfiles.Where(profile => profile.UserId == world.UserA1)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            var thrown = await delete.Should().ThrowAsync<PostgresException>();
            thrown.Which.SqlState.Should().Be("42501");
        }

        await using var check = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var user = await check.Db.Users.SingleAsync(TestContext.Current.CancellationToken);
        user.DisplayName.Should().Be($"User {world.UserA1:N}");
        user.Profile.UserId.Should().Be(world.UserA1);
    }

    [Fact]
    public async Task Members_see_only_safe_profiles_in_the_selected_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var profiles = await session.Db.UserProfiles.ToListAsync(TestContext.Current.CancellationToken);
        profiles.Select(profile => profile.UserId).Should().BeEquivalentTo([world.UserA1, world.UserA2, world.Traveler]);
        profiles.Should().OnlyContain(profile => profile.DisplayName == $"User {profile.UserId:N}");
        var columns = await session.Db.Database.SqlQueryRaw<string>(
            "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'user_profiles'")
            .ToListAsync(TestContext.Current.CancellationToken);
        columns.Should().BeEquivalentTo(["user_id", "display_name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_context_does_not_reveal_fellow_profiles(bool setUser)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, spaceId: null, setUser ? world.UserA1 : null);
        var ids = await session.Db.UserProfiles.Select(profile => profile.UserId).ToListAsync(TestContext.Current.CancellationToken);
        ids.Should().BeEquivalentTo(setUser ? [world.UserA1] : Array.Empty<Guid>());
    }

    [Fact]
    public async Task A_space_without_a_user_does_not_reveal_profiles()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, userId: null);
        (await session.Db.UserProfiles.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task A_nonmember_cannot_discover_profiles_by_selecting_another_space()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceB, world.UserA1);
        var ids = await session.Db.UserProfiles.Select(profile => profile.UserId).ToListAsync(TestContext.Current.CancellationToken);
        ids.Should().Equal(world.UserA1);
    }

    [Fact]
    public async Task Changing_an_identity_display_name_updates_its_profile()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using (var owner = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            (await owner.Db.Users.SingleAsync(TestContext.Current.CancellationToken)).DisplayName = "Updated profile";
            await owner.Commit();
        }

        await using var member = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await member.Db.UserProfiles.SingleAsync(profile => profile.UserId == world.UserA2, TestContext.Current.CancellationToken))
            .DisplayName.Should().Be("Updated profile");
        var changed = await member.Db.UserProfiles.Where(profile => profile.UserId == world.UserA2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.DisplayName, "forbidden"), TestContext.Current.CancellationToken);
        changed.Should().Be(0);
    }

    [Fact]
    public async Task Profile_schema_and_policies_enforce_the_declared_boundary()
    {
        await using var provider = postgres.BuildServices();
        await using var session = await TenantSession.Open(provider, spaceId: null, userId: null);
        var valid = await session.Db.Database.SqlQueryRaw<bool>(
            """
            SELECT (
              EXISTS (SELECT 1 FROM pg_class WHERE oid = 'public.user_profiles'::regclass
                AND relrowsecurity AND relforcerowsecurity)
              AND EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'user_profiles'
                  AND column_name = 'user_id' AND data_type = 'uuid' AND is_nullable = 'NO')
              AND (SELECT count(*) FROM pg_policy WHERE polrelid = 'public.user_profiles'::regclass) = 2
              AND EXISTS (SELECT 1 FROM pg_policy WHERE polrelid = 'public.user_profiles'::regclass
                AND polname = 'profile_self' AND polcmd = '*' AND polpermissive
                AND pg_get_expr(polqual, polrelid) = '(user_id = sys.current_user_id())'
                AND pg_get_expr(polwithcheck, polrelid) = '(user_id = sys.current_user_id())')
              AND EXISTS (SELECT 1 FROM pg_policy WHERE polrelid = 'public.user_profiles'::regclass
                AND polname = 'member_profiles' AND polcmd = 'r' AND polpermissive AND polwithcheck IS NULL)
              AND has_table_privilege(current_user, 'public.user_profiles', 'SELECT')
              AND has_table_privilege(current_user, 'public.user_profiles', 'INSERT')
              AND has_table_privilege(current_user, 'public.user_profiles', 'UPDATE')
              AND NOT has_table_privilege(current_user, 'public.user_profiles', 'DELETE')
            ) AS "Value"
            """).SingleAsync(TestContext.Current.CancellationToken);
        valid.Should().BeTrue();
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("delete")]
    [InlineData("reassign")]
    public async Task Foreign_profile_writes_are_refused(string operation)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var sql = operation switch
        {
            "insert" => $"INSERT INTO user_profiles (user_id, display_name) VALUES ('{world.UserA2}', 'forbidden')",
            "delete" => $"DELETE FROM user_profiles WHERE user_id = '{world.UserA2}'",
            _ => $"UPDATE user_profiles SET user_id = '{world.UserA2}' WHERE user_id = '{world.UserA1}'",
        };
        var write = () => session.Db.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
        var thrown = await write.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    public async Task Missing_user_context_cannot_write_profiles(string operation)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, userId: null);
        if (operation == "insert")
        {
            var insert = () => session.Db.Database.ExecuteSqlAsync(
                $"INSERT INTO user_profiles (user_id, display_name) VALUES ({world.UserA1}, 'forbidden')",
                TestContext.Current.CancellationToken);
            var thrown = await insert.Should().ThrowAsync<PostgresException>();
            thrown.Which.SqlState.Should().Be("42501");
        }
        else
        {
            (await session.Db.UserProfiles.ExecuteUpdateAsync(
                setters => setters.SetProperty(profile => profile.DisplayName, "forbidden"),
                TestContext.Current.CancellationToken)).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Own_profile_creation_read_and_update_work_with_or_without_a_space(int selectedSpace)
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var userId = Guid.CreateVersion7();
        var spaceId = selectedSpace switch { 1 => world.SpaceA, 2 => world.SpaceB, _ => (Guid?)null };
        await using (var session = await TenantSession.Open(provider, spaceId, userId))
        {
            session.Db.Users.Add(new User { Id = userId, DisplayName = "Created" });
            await session.Commit();
        }

        await using (var session = await TenantSession.Open(provider, spaceId, userId))
        {
            var user = await session.Db.Users.SingleAsync(TestContext.Current.CancellationToken);
            user.DisplayName.Should().Be("Created");
            user.DisplayName = "Updated";
            await session.Commit();
        }

        await using var check = await TenantSession.Open(provider, spaceId, userId);
        (await check.Db.UserProfiles.SingleAsync(TestContext.Current.CancellationToken)).DisplayName.Should().Be("Updated");
    }

    [Fact]
    public async Task Space_B_members_discover_only_their_fellow_profiles()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceB, world.UserB1);
        var ids = await session.Db.UserProfiles.Select(profile => profile.UserId).ToListAsync(TestContext.Current.CancellationToken);
        ids.Should().BeEquivalentTo([world.UserB1, world.Traveler]);
    }

    #endregion
}
