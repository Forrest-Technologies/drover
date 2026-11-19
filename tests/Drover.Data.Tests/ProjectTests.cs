namespace Drover.Data.Tests;

/// <summary>
/// Projects and conversation membership at the database boundary: space RLS isolation across spaces, multi-member
/// visibility within one shared space, same-space composite foreign keys, direct role writes, and
/// <c>sys.clear_project_threads</c>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ProjectTests(PostgresFixture postgres)
{
    #region Tenancy And Foreign Keys

    [Fact]
    public async Task A_project_and_its_conversation_membership_are_isolated_by_space_and_members_share_project_reads()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        Guid projectAId;
        Guid threadA1PrivateId;
        Guid threadA2PrivateId;
        Bot botA;

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var project = new Project { Name = "Alpha", Instructions = "Secret space A guide." };
            session.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(session.Db, bot, []);
            session.Db.Projects.Add(project);
            await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            thread.ProjectId = project.Id;
            session.Db.Threads.Add(thread);
            await session.Commit();

            projectAId = project.Id;
            botA = bot;
            threadA1PrivateId = thread.Id;
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var visibleProjects = await asA2.Db.Projects.Select(p => p.Id).ToListAsync(TestContext.Current.CancellationToken);
            var threadA2 = CoreRows.NewThread(botA, world.UserA2, world.UserA2);
            threadA2.ProjectId = projectAId;
            asA2.Db.Threads.Add(threadA2);
            await asA2.Commit();
            threadA2PrivateId = threadA2.Id;

            visibleProjects.Should().Equal([projectAId], "every member of the space sees the project");
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var projectThreads = await asA2.Db.Threads
                .Where(t => t.ProjectId == projectAId)
                .Select(t => t.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            projectThreads.Should().Equal([threadA2PrivateId], "member sees only their own private thread in the project");
        }

        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var projectsInB = await asB.Db.Projects.ToListAsync(TestContext.Current.CancellationToken);
            var threadsInB = await asB.Db.Threads.Where(t => t.ProjectId == projectAId).ToListAsync(TestContext.Current.CancellationToken);
            var botInB = CoreRows.NewBot(CoreRows.NewConnection());
            asB.Db.ModelConnections.Add(new ModelConnection { Id = botInB.ModelConnectionId, Name = "conn", Kind = "openai", DefaultModel = "m" });
            CoreRows.AddBot(asB.Db, botInB, []);
            await asB.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var crossSpaceFk = async () =>
            {
                var bad = CoreRows.NewThread(botInB, world.UserB1, world.UserB1);
                bad.ProjectId = projectAId;
                asB.Db.Threads.Add(bad);
                await asB.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
            };

            projectsInB.Should().BeEmpty();
            threadsInB.Should().BeEmpty();
            (await crossSpaceFk.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }

        threadA1PrivateId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Direct_role_writes_into_another_space_are_blocked_by_row_level_security()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var projectId = Guid.CreateVersion7();

        await using (var asA = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            asA.Db.Projects.Add(new Project { Id = projectId, Name = "Owned by A" });
            await asA.Commit();
        }

        await using var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1);
        var insertOther = () => asB.Execute(
            "INSERT INTO projects (id, space_id, name, instructions) VALUES (@id, @space, 'Intruder', '')",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceA));
        var updateOther = await asB.Execute(
            "UPDATE projects SET name = 'Hijacked' WHERE id = @id",
            ("id", projectId));
        var deleteOther = await asB.Execute(
            "DELETE FROM projects WHERE id = @id",
            ("id", projectId));

        (await insertOther.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        updateOther.Should().Be(0);
        deleteOther.Should().Be(0);
    }

    #endregion

    #region Clearing On Removal

    [Fact]
    public async Task Clearing_project_threads_preserves_every_audience_in_the_callers_space_and_leaves_other_spaces_alone()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        Guid projectAId;
        Guid projectBId;
        Guid threadA1Id;
        Guid threadA2Id;
        Guid threadBId;

        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var project = new Project { Name = "Project A" };
            asA1.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(asA1.Db, bot, []);
            asA1.Db.Projects.Add(project);
            await asA1.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            thread.ProjectId = project.Id;
            asA1.Db.Threads.Add(thread);
            await asA1.Commit();
            projectAId = project.Id;
            threadA1Id = thread.Id;

            await using var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
            var second = CoreRows.NewThread(bot, world.UserA2, world.UserA2);
            second.ProjectId = project.Id;
            asA2.Db.Threads.Add(second);
            await asA2.Commit();
            threadA2Id = second.Id;
        }

        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var project = new Project { Name = "Project B" };
            asB.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(asB.Db, bot, []);
            asB.Db.Projects.Add(project);
            await asB.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var thread = CoreRows.NewThread(bot, world.UserB1, world.UserB1);
            thread.ProjectId = project.Id;
            asB.Db.Threads.Add(thread);
            await asB.Commit();
            projectBId = project.Id;
            threadBId = thread.Id;
        }

        // Space B trying to clear Space A's project touches nothing.
        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var wrongSpace = await asB.Db.Database
                .SqlQuery<int>($"SELECT sys.clear_project_threads({projectAId}) AS \"Value\"")
                .SingleAsync(TestContext.Current.CancellationToken);
            wrongSpace.Should().Be(0);
            await asB.Commit();
        }

        // User A1 removing Project A clears both User A1's and User A2's private threads without deleting either thread.
        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var project = await asA1.Db.Projects.SingleAsync(p => p.Id == projectAId, TestContext.Current.CancellationToken);
            var cleared = await asA1.Db.Database
                .SqlQuery<int>($"SELECT sys.clear_project_threads({projectAId}) AS \"Value\"")
                .SingleAsync(TestContext.Current.CancellationToken);
            asA1.Db.Projects.Remove(project);
            await asA1.Commit();

            cleared.Should().Be(2);
        }

        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var survived = await asA1.Db.Threads.SingleAsync(t => t.Id == threadA1Id, TestContext.Current.CancellationToken);
            survived.ProjectId.Should().BeNull();
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var survived = await asA2.Db.Threads.SingleAsync(t => t.Id == threadA2Id, TestContext.Current.CancellationToken);
            survived.ProjectId.Should().BeNull("member's private thread stays intact and leaves the removed project");
        }

        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var untouched = await asB.Db.Threads.SingleAsync(t => t.Id == threadBId, TestContext.Current.CancellationToken);
            untouched.ProjectId.Should().Be(projectBId);
        }
    }

    #endregion
}
