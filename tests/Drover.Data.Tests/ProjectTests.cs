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
            await asB.Db.Database.ExecuteSqlAsync($"SELECT sys.clear_project_threads({projectAId})", TestContext.Current.CancellationToken);
            await asB.Commit();
        }

        // User A1 removing Project A clears both User A1's and User A2's private threads without deleting either thread.
        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var project = await asA1.Db.Projects.SingleAsync(p => p.Id == projectAId, TestContext.Current.CancellationToken);
            await asA1.Db.Database.ExecuteSqlAsync($"SELECT sys.clear_project_threads({projectAId})", TestContext.Current.CancellationToken);
            asA1.Db.Projects.Remove(project);
            await asA1.Commit();
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

    [Fact]
    public async Task A_same_space_non_owner_cannot_change_a_shared_threads_project_at_the_database_boundary()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        Guid projectOneId;
        Guid projectTwoId;
        Guid sharedInProjectId;
        Guid sharedUnassignedId;

        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var projectOne = new Project { Name = "Project One" };
            var projectTwo = new Project { Name = "Project Two" };
            asA1.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(asA1.Db, bot, []);
            asA1.Db.Projects.AddRange(projectOne, projectTwo);
            await asA1.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var inProject = CoreRows.NewThread(bot, world.UserA1, Audience.Everyone);
            inProject.ProjectId = projectOne.Id;
            var unassigned = CoreRows.NewThread(bot, world.UserA1, Audience.Everyone);
            asA1.Db.Threads.AddRange(inProject, unassigned);
            await asA1.Commit();

            projectOneId = projectOne.Id;
            projectTwoId = projectTwo.Id;
            sharedInProjectId = inProject.Id;
            sharedUnassignedId = unassigned.Id;
        }

        // Another member in the same space can see and update shared columns such as model, but cannot move the owner's thread.
        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var moveOther = () => asA2.Execute(
                "UPDATE threads SET project_id = @project WHERE id = @thread",
                ("project", projectTwoId),
                ("thread", sharedInProjectId));
            (await moveOther.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var clearOther = () => asA2.Execute(
                "UPDATE threads SET project_id = NULL WHERE id = @thread",
                ("thread", sharedInProjectId));
            (await clearOther.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var assignOther = () => asA2.Execute(
                "UPDATE threads SET project_id = @project WHERE id = @thread",
                ("project", projectOneId),
                ("thread", sharedUnassignedId));
            (await assignOther.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var updated = await asA2.Execute(
                "UPDATE threads SET model = 'chosen-by-member' WHERE id = @thread",
                ("thread", sharedInProjectId));
            updated.Should().Be(1, "other permitted column updates on shared threads still succeed");
            await asA2.Commit();
        }

        // Even after another member's attempts, both shared threads keep their original projects; the owner can still move hers.
        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var storedAssigned = await asA1.Db.Threads.SingleAsync(t => t.Id == sharedInProjectId, TestContext.Current.CancellationToken);
            var storedUnassigned = await asA1.Db.Threads.SingleAsync(t => t.Id == sharedUnassignedId, TestContext.Current.CancellationToken);
            storedAssigned.ProjectId.Should().Be(projectOneId);
            storedUnassigned.ProjectId.Should().BeNull();

            storedAssigned.ProjectId = projectTwoId;
            await asA1.Commit();
        }
    }

    [Fact]
    public async Task A_space_member_is_refused_from_calling_clear_project_threads_and_it_returns_void()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var adminUserId = Guid.CreateVersion7();
        Guid projectAId;
        Guid ownerThreadId;
        Guid memberThreadId;

        await using (var asNewAdmin = await TenantSession.Open(provider, world.SpaceA, adminUserId))
        {
            asNewAdmin.Db.Users.Add(TenantWorld.NewUser(adminUserId));
            await asNewAdmin.Commit();
        }

        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            asA1.Db.Memberships.Add(new Membership { UserId = adminUserId, Role = MembershipRole.Admin });

            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var project = new Project { Name = "Guarded Project" };
            asA1.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(asA1.Db, bot, []);
            asA1.Db.Projects.Add(project);
            await asA1.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var ownerThread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
            ownerThread.ProjectId = project.Id;
            asA1.Db.Threads.Add(ownerThread);
            await asA1.Commit();

            projectAId = project.Id;
            ownerThreadId = ownerThread.Id;

            await using var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
            var memberThread = CoreRows.NewThread(bot, world.UserA2, world.UserA2);
            memberThread.ProjectId = project.Id;
            asA2.Db.Threads.Add(memberThread);
            await asA2.Commit();
            memberThreadId = memberThread.Id;
        }

        // The function signature returns void so callers learn no private thread count.
        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var resultType = await asA1.Db.Database
                .SqlQueryRaw<string>(
                    "SELECT pg_get_function_result(p.oid) AS \"Value\" FROM pg_proc p WHERE p.pronamespace = 'sys'::regnamespace AND p.proname = 'clear_project_threads'")
                .SingleAsync(TestContext.Current.CancellationToken);
            resultType.Should().Be("void");
        }

        // A regular member in the same space cannot invoke clear_project_threads.
        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var callAsMember = () => asA2.Execute(
                "SELECT sys.clear_project_threads(@project)",
                ("project", projectAId));
            (await callAsMember.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // A connection with no tenant context is also refused.
        await using (var unauthenticated = await TenantSession.Open(provider, spaceId: null, userId: null))
        {
            var callWithoutTenant = () => unauthenticated.Execute(
                "SELECT sys.clear_project_threads(@project)",
                ("project", projectAId));
            (await callWithoutTenant.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Both private threads keep their project link after member refusal.
        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await asA1.Db.Threads.SingleAsync(t => t.Id == ownerThreadId, TestContext.Current.CancellationToken)).ProjectId.Should().Be(projectAId);
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            (await asA2.Db.Threads.SingleAsync(t => t.Id == memberThreadId, TestContext.Current.CancellationToken)).ProjectId.Should().Be(projectAId);
        }

        // A space admin is authorized to clear project threads across audiences.
        await using (var asAdmin = await TenantSession.Open(provider, world.SpaceA, adminUserId))
        {
            await asAdmin.Db.Database.ExecuteSqlAsync($"SELECT sys.clear_project_threads({projectAId})", TestContext.Current.CancellationToken);
            await asAdmin.Commit();
        }

        await using (var asA1 = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await asA1.Db.Threads.SingleAsync(t => t.Id == ownerThreadId, TestContext.Current.CancellationToken)).ProjectId.Should().BeNull();
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            (await asA2.Db.Threads.SingleAsync(t => t.Id == memberThreadId, TestContext.Current.CancellationToken)).ProjectId.Should().BeNull();
        }
    }

    #endregion
}
