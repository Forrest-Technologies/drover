namespace Drover.Data.Tests;

/// <summary>
/// A file a person attaches is seen by exactly the people who see its conversation, and the space's total of stored
/// bytes counts every member's files without anyone reading them.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class FileRowTests(PostgresFixture postgres)
{
    #region Audience

    [Fact]
    public async Task A_file_in_a_private_conversation_is_seen_by_its_owner_only_until_the_conversation_is_shared()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var (thread, file) = await SaveThreadWithFile(provider, world, shared: false);

        (await CanSee(provider, world.SpaceA, world.UserA1, file)).Should().BeTrue();
        (await CanSee(provider, world.SpaceA, world.UserA2, file)).Should().BeFalse("another member cannot see the conversation");
        (await CanSee(provider, world.SpaceB, world.UserB1, file)).Should().BeFalse("another space never sees it");

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Execute("UPDATE threads SET audience_user_id = @everyone WHERE id = @thread", ("everyone", Audience.Everyone), ("thread", thread.Id));
            await session.Commit();
        }

        (await CanSee(provider, world.SpaceA, world.UserA2, file)).Should().BeTrue("sharing the conversation shares its files");
        (await CanSee(provider, world.SpaceB, world.UserB1, file)).Should().BeFalse();
    }

    [Fact]
    public async Task A_file_cannot_be_given_a_narrower_audience_than_its_conversation()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var (thread, _) = await SaveThreadWithFile(provider, world, shared: true);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA2);

        var hidden = () => session.Execute(
            "INSERT INTO files (id, space_id, audience_user_id, thread_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @audience, @thread, 'k', 'n', 'text/plain', 1, 'x')",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceA),
            ("audience", world.UserA2),
            ("thread", thread.Id));

        var thrown = await hidden.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_file_cannot_name_another_spaces_conversation()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var (thread, _) = await SaveThreadWithFile(provider, world, shared: true);
        await using var session = await TenantSession.Open(provider, world.SpaceB, world.UserB1);

        var foreign = () => session.Execute(
            "INSERT INTO files (id, space_id, audience_user_id, thread_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @everyone, @thread, 'k', 'n', 'text/plain', 1, 'x')",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceB),
            ("everyone", Audience.Everyone),
            ("thread", thread.Id));

        var thrown = await foreign.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    #endregion

    #region Stored Bytes

    [Fact]
    public async Task The_spaces_total_counts_another_members_private_files_and_stops_at_the_limit()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        (await Reserve(provider, world.SpaceA, world.UserA1, 600, limit: 1000)).Should().BeTrue();
        (await Reserve(provider, world.SpaceA, world.UserA2, 500, limit: 1000)).Should().BeFalse();
        (await Reserve(provider, world.SpaceA, world.UserA2, 400, limit: 1000)).Should().BeTrue();
        (await Reserve(provider, world.SpaceB, world.UserB1, 1000, limit: 1000)).Should().BeTrue("each space has its own total");

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA2);
        (await new FileRepository(session.Db).GetStoredBytes(world.SpaceA, Token)).Should().Be(1000);
    }

    [Fact]
    public async Task One_space_cannot_change_another_spaces_total()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        (await Reserve(provider, world.SpaceB, world.UserB1, 10, limit: 1000, spaceNamed: world.SpaceA)).Should().BeFalse();

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await new FileRepository(session.Db).GetStoredBytes(world.SpaceA, Token)).Should().Be(0);
    }

    [Fact]
    public async Task Giving_bytes_back_never_takes_the_total_below_zero()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await Reserve(provider, world.SpaceA, world.UserA1, 100, limit: 1000);

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var files = new FileRepository(session.Db);
        await files.ReleaseBytes(world.SpaceA, 500, Token);

        (await files.GetStoredBytes(world.SpaceA, Token)).Should().Be(0);
    }

    #endregion

    #region Project Files

    [Fact]
    public async Task A_project_file_is_visible_to_every_member_of_its_space_and_isolated_from_other_spaces()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        Guid projectId;
        Guid fileId;

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var project = new Project { Name = "Shared Handbook" };
            session.Db.Projects.Add(project);
            await session.Db.SaveChangesAsync(Token);

            fileId = Guid.CreateVersion7();
            session.Db.Files.Add(new StoredFile
            {
                Id = fileId,
                AudienceUserId = Audience.Everyone,
                ProjectId = project.Id,
                StorageKey = FileStorageKeys.For(world.SpaceA, fileId),
                Name = "guide.txt",
                ContentType = "text/plain",
                Size = 12,
                Sha256 = new string('1', 64),
                CreatedByUserId = world.UserA1,
            });
            await session.Commit();
            projectId = project.Id;
        }

        await using (var asA2 = await TenantSession.Open(provider, world.SpaceA, world.UserA2))
        {
            var repo = new FileRepository(asA2.Db);
            (await repo.ListProjectFiles(projectId, Token)).Should().ContainSingle().Which.Id.Should().Be(fileId);
            (await repo.FindProjectFile(projectId, fileId, Token)).Should().NotBeNull();
        }

        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var repo = new FileRepository(asB.Db);
            (await repo.ListProjectFiles(projectId, Token)).Should().BeEmpty();
            (await repo.FindProjectFile(projectId, fileId, Token)).Should().BeNull();
        }
    }

    [Fact]
    public async Task A_project_file_enforces_same_space_foreign_key_exclusive_owner_and_shared_audience()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        Guid projectAId;
        Guid threadAId;

        await using (var asA = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var connection = CoreRows.NewConnection();
            var bot = CoreRows.NewBot(connection);
            var project = new Project { Name = "Project A" };
            asA.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(asA.Db, bot, []);
            asA.Db.Projects.Add(project);
            await asA.Db.SaveChangesAsync(Token);

            var thread = CoreRows.NewThread(bot, world.UserA1, Audience.Everyone);
            asA.Db.Threads.Add(thread);
            await asA.Commit();
            projectAId = project.Id;
            threadAId = thread.Id;
        }

        // Cross-space project reference is rejected by fk_files_projects_space_id_project_id.
        await using (var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1))
        {
            var crossSpace = () => asB.Execute(
                "INSERT INTO files (id, space_id, audience_user_id, project_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @everyone, @project, 'k1', 'a.txt', 'text/plain', 1, 'x')",
                ("id", Guid.CreateVersion7()),
                ("space", world.SpaceB),
                ("everyone", Audience.Everyone),
                ("project", projectAId));

            (await crossSpace.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }

        await using (var asA = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var privateAudience = () => asA.Execute(
                "INSERT INTO files (id, space_id, audience_user_id, project_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @user, @project, 'k2', 'b.txt', 'text/plain', 1, 'x')",
                ("id", Guid.CreateVersion7()),
                ("space", world.SpaceA),
                ("user", world.UserA1),
                ("project", projectAId));

            (await privateAudience.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var asA = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var bothOwners = () => asA.Execute(
                "INSERT INTO files (id, space_id, audience_user_id, thread_id, project_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @everyone, @thread, @project, 'k3', 'c.txt', 'text/plain', 1, 'x')",
                ("id", Guid.CreateVersion7()),
                ("space", world.SpaceA),
                ("everyone", Audience.Everyone),
                ("thread", threadAId),
                ("project", projectAId));

            (await bothOwners.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }

        await using (var asA = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var withMessage = () => asA.Execute(
                "INSERT INTO files (id, space_id, audience_user_id, project_id, message_id, storage_key, name, content_type, size, sha256) VALUES (@id, @space, @everyone, @project, @message, 'k4', 'd.txt', 'text/plain', 1, 'x')",
                ("id", Guid.CreateVersion7()),
                ("space", world.SpaceA),
                ("everyone", Audience.Everyone),
                ("project", projectAId),
                ("message", Guid.CreateVersion7()));

            (await withMessage.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(ChatThread Thread, StoredFile File)> SaveThreadWithFile(IServiceProvider provider, TenantWorld world, bool shared)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var thread = CoreRows.NewThread(bot, world.UserA1, shared ? Audience.Everyone : world.UserA1);
        session.Db.Threads.Add(thread);
        var id = Guid.CreateVersion7();
        var file = new StoredFile
        {
            Id = id,
            AudienceUserId = thread.AudienceUserId,
            ThreadId = thread.Id,
            StorageKey = FileStorageKeys.For(world.SpaceA, id),
            Name = "notes.txt",
            ContentType = "text/plain",
            Size = 5,
            Sha256 = new string('0', 64),
            CreatedByUserId = world.UserA1,
        };
        session.Db.Files.Add(file);
        await session.Commit();
        return (thread, file);
    }

    private static async Task<bool> CanSee(IServiceProvider provider, Guid space, Guid viewer, StoredFile file)
    {
        await using var session = await TenantSession.Open(provider, space, viewer);
        return await new FileRepository(session.Db).FindFile(file.Id, Token) is not null;
    }

    private static async Task<bool> Reserve(IServiceProvider provider, Guid space, Guid user, long bytes, long limit, Guid? spaceNamed = null)
    {
        await using var session = await TenantSession.Open(provider, space, user);
        var reserved = await new FileRepository(session.Db).ReserveBytes(spaceNamed ?? space, bytes, limit, Token);
        await session.Commit();
        return reserved;
    }

    #endregion
}
