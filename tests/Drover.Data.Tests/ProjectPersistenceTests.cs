namespace Drover.Data.Tests;

/// <summary>Project membership privileges that are narrower than a conversation's shared audience.</summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ProjectPersistenceTests(PostgresFixture postgres)
{
    #region Public Methods

    [Fact]
    public async Task Only_the_conversation_owner_can_update_project_membership_as_the_app_role()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var (thread, project) = await SaveSharedThreadAndProject(provider, world);

        await using (var member = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var direct = () => member.Execute(
                "UPDATE threads SET project_id = @project WHERE id = @thread",
                ("project", project.Id),
                ("thread", thread.Id));

            var thrown = await direct.Should().ThrowAsync<PostgresException>();
            thrown.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var member = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var changed = await member.Execute(
                "UPDATE owned_thread_projects SET project_id = @project WHERE id = @thread",
                ("project", project.Id),
                ("thread", thread.Id));
            changed.Should().Be(0);
            await member.Commit();
        }

        await using (var owner = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var changed = await owner.Execute(
                "UPDATE owned_thread_projects SET project_id = @project WHERE id = @thread",
                ("project", project.Id),
                ("thread", thread.Id));
            changed.Should().Be(1);
            await owner.Commit();
        }

        await using var verify = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await verify.Db.Threads.SingleAsync(found => found.Id == thread.Id, TestContext.Current.CancellationToken))
            .ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task The_owner_view_has_the_expected_security_boundary_and_minimal_grants()
    {
        await using var connection = new NpgsqlConnection(postgres.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_userbyid(c.relowner) = 'drover_owner'
               AND c.reloptions @> ARRAY['security_barrier=true']
               AND v.check_option = 'LOCAL'
               AND v.is_updatable = 'YES'
               AND has_column_privilege('drover_app', 'owned_thread_projects', 'id', 'SELECT')
               AND has_column_privilege('drover_app', 'owned_thread_projects', 'project_id', 'UPDATE')
               AND NOT has_column_privilege('drover_app', 'owned_thread_projects', 'project_id', 'SELECT')
               AND NOT has_column_privilege('drover_app', 'threads', 'project_id', 'UPDATE')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN information_schema.views v ON v.table_schema = n.nspname AND v.table_name = c.relname
            WHERE n.nspname = 'public' AND c.relname = 'owned_thread_projects'
            """,
            connection);

        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(true);
    }

    #endregion

    #region Private Methods

    private static async Task<(ChatThread Thread, Project Project)> SaveSharedThreadAndProject(
        IServiceProvider provider,
        TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        var thread = CoreRows.NewThread(bot, world.UserA1, Audience.Everyone);
        var project = new Project { Name = "Shared", Instructions = string.Empty };
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        session.Db.Threads.Add(thread);
        session.Db.Projects.Add(project);
        await session.Commit();
        return (thread, project);
    }

    #endregion
}
