namespace Drover.Engine.Tests.Files;

/// <summary>Removal and durable storage cleanup for conversation uploads that were never sent.</summary>
public sealed class UploadCleanupTests(PostgresServer postgres)
{
    #region Public Methods

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unsent_upload_older_than_one_day_loses_its_row_bytes_and_quota(bool shared)
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = shared ? await world.NewSharedThread() : await world.NewThread();
        var uploaded = (await world.Upload(threadId, "old.txt", "old upload"u8.ToArray())).File!;
        await MakeOld(world, uploaded.Id);

        var removed = await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token);

        removed.Should().Be(1);
        world.Files.Count.Should().Be(0);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.PendingUploadDeletions.CountAsync(RunWorld.Token))).Should().Be(0);
        (await StoredBytes(world)).Should().Be(0);
    }

    [Fact]
    public async Task Fresh_unsent_sent_and_project_files_stay()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var oldThread = await world.NewThread();
        var sentThread = await world.NewThread();
        var freshThread = await world.NewThread();
        var old = (await world.Upload(oldThread, "old.txt", "old"u8.ToArray())).File!;
        var sent = (await world.Upload(sentThread, "sent.txt", "sent"u8.ToArray())).File!;
        var fresh = (await world.Upload(freshThread, "fresh.txt", "fresh"u8.ToArray())).File!;
        var posted = await world.PostRaw(sentThread, "sent", fileIds: [sent.Id]);
        posted.Problem.Should().Be(PostProblem.None, posted.Detail);
        var project = await CreateProject(world);
        var projectFile = (await UploadProjectFile(world, project.Id, "project.txt", "project"u8.ToArray())).File!;
        await MakeOld(world, old.Id, sent.Id, projectFile.Id);

        var removed = await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token);

        removed.Should().Be(1);
        var remaining = await world.Read(db => db.Files.AsNoTracking()
            .Select(file => file.Id).ToListAsync(RunWorld.Token));
        remaining.Should().BeEquivalentTo([sent.Id, fresh.Id, projectFile.Id]);
        world.Files.Count.Should().Be(3);
        (await StoredBytes(world)).Should().Be(sent.Size + fresh.Size + projectFile.Size);
    }

    [Fact]
    public async Task A_cancelled_storage_delete_keeps_one_receipt_for_a_new_host_and_does_not_stop_the_pass()
    {
        var store = new InterruptingStore { CancellationsRemaining = 1 };
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(store));
        var threadId = await world.NewThread();
        var first = (await world.Upload(threadId, "first.txt", "first"u8.ToArray())).File!;
        var second = (await world.Upload(threadId, "second.txt", "second"u8.ToArray())).File!;
        await MakeOld(world, first.Id, second.Id);

        var removed = await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token);

        removed.Should().Be(2);
        store.Count.Should().Be(1, "the second deletion in the pass still runs");
        (await world.Read(db => db.PendingUploadDeletions.CountAsync(RunWorld.Token))).Should().Be(1);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        (await StoredBytes(world)).Should().Be(0, "quota is released when rows are staged, exactly once");

        await world.Read(db => db.PendingUploadDeletions.ExecuteUpdateAsync(
            set => set.SetProperty(
                pending => pending.CleanupClaimUntil, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1)),
            RunWorld.Token));

        var restarted = world.IdleHost();
        (await restarted.Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token)).Should().Be(0);
        store.Count.Should().Be(0);
        (await world.Read(db => db.PendingUploadDeletions.CountAsync(RunWorld.Token))).Should().Be(0);
        (await StoredBytes(world)).Should().Be(0);
    }

    [Fact]
    public async Task One_pass_removes_private_uploads_from_two_spaces_without_cross_tenant_reads()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var firstThread = await world.NewThread();
        var first = (await world.Upload(firstThread, "first.txt", "first"u8.ToArray())).File!;
        var secondSpace = await SeedPrivateSpace(world);
        var second = (await UploadAs(
            world,
            secondSpace.SpaceId,
            secondSpace.UserId,
            secondSpace.ThreadId,
            "second.txt",
            "second"u8.ToArray())).File!;
        await MakeOld(world, first.Id);
        await MakeOldAs(world, secondSpace.SpaceId, secondSpace.UserId, second.Id);

        (await world.Read(db => db.Files.AnyAsync(file => file.Id == second.Id, RunWorld.Token)))
            .Should().BeFalse("the first space cannot read the second space's private upload");
        first.AudienceUserId.Should().Be(world.UserId);
        second.AudienceUserId.Should().Be(secondSpace.UserId);

        (await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token)).Should().Be(2);

        world.Files.Count.Should().Be(0);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        (await StoredBytes(world)).Should().Be(0);
        var secondState = await world.TransactAs(
            secondSpace.SpaceId,
            secondSpace.UserId,
            async services =>
            {
                var db = services.GetRequiredService<DroverDbContext>();
                return (
                    await db.Files.CountAsync(RunWorld.Token),
                    await db.PendingUploadDeletions.CountAsync(RunWorld.Token),
                    await db.Spaces.Where(space => space.Id == secondSpace.SpaceId)
                        .Select(space => space.StoredFileBytes)
                        .SingleAsync(RunWorld.Token));
            });
        secondState.Should().Be((0, 0, 0L));
    }

    [Fact]
    public async Task A_thread_locked_for_message_work_is_skipped_then_a_sent_file_is_preserved()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var upload = (await world.Upload(threadId, "kept.txt", "kept"u8.ToArray())).File!;
        await MakeOld(world, upload.Id);
        await using var heldScope = world.Hosts[0].Services.CreateAsyncScope();
        heldScope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        await using var held = await heldScope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        (await heldScope.ServiceProvider.GetRequiredService<ThreadRepository>()
            .GetThreadForFileChange(threadId, RunWorld.Token)).Should().NotBeNull();

        (await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token)).Should().Be(0);
        await held.Commit(RunWorld.Token);
        var sent = await world.PostRaw(threadId, "send", fileIds: [upload.Id]);
        sent.Problem.Should().Be(PostProblem.None, sent.Detail);

        (await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token)).Should().Be(0);
        (await world.Read(db => db.Files.SingleAsync(file => file.Id == upload.Id, RunWorld.Token)))
            .MessageId.Should().Be(sent.Message!.Id);
        world.Files.Count.Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_that_wins_before_send_makes_the_message_refuse_the_missing_file()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var upload = (await world.Upload(threadId, "gone.txt", "gone"u8.ToArray())).File!;
        await MakeOld(world, upload.Id);

        (await world.Hosts[0].Services.GetRequiredService<UploadCleanup>().Sweep(RunWorld.Token)).Should().Be(1);
        var sent = await world.PostRaw(threadId, "send", fileIds: [upload.Id]);

        sent.Problem.Should().Be(PostProblem.Invalid);
        sent.Detail.Should().Contain("Attach it again");
        (await world.Read(db => db.Messages.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task The_cleanup_thread_lock_does_not_block_an_upload_foreign_key_check()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        await using var heldScope = world.Hosts[0].Services.CreateAsyncScope();
        heldScope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        await using var held = await heldScope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        (await heldScope.ServiceProvider.GetRequiredService<ThreadRepository>()
            .GetThreadForFileChange(threadId, RunWorld.Token)).Should().NotBeNull();

        var upload = world.Upload(threadId, "concurrent.txt", "concurrent"u8.ToArray());
        var finished = await Task.WhenAny(upload, Task.Delay(TimeSpan.FromSeconds(10), RunWorld.Token));

        finished.Should().Be(upload, "NO KEY UPDATE is compatible with the file insert's KEY SHARE lock");
        (await upload).File.Should().NotBeNull();
        await held.Commit(RunWorld.Token);
    }

    #endregion

    #region Private Methods

    private static Task<long> StoredBytes(RunWorld world) =>
        world.Read(db => db.Spaces
            .Where(space => space.Id == world.SpaceId)
            .Select(space => space.StoredFileBytes)
            .SingleAsync(RunWorld.Token));

    private static Task<int> MakeOld(RunWorld world, params Guid[] fileIds) =>
        world.Read(db => db.Files
            .Where(file => fileIds.Contains(file.Id))
            .ExecuteUpdateAsync(
                set => set.SetProperty(
                    file => file.CreatedAt,
                    DateTimeOffset.UtcNow - UploadCleanup.Retention - TimeSpan.FromMinutes(1)),
                RunWorld.Token));

    private static Task<int> MakeOldAs(
        RunWorld world,
        Guid spaceId,
        Guid userId,
        params Guid[] fileIds) =>
        world.TransactAs(
            spaceId,
            userId,
            services => services.GetRequiredService<DroverDbContext>().Files
                .Where(file => fileIds.Contains(file.Id))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(
                        file => file.CreatedAt,
                        DateTimeOffset.UtcNow - UploadCleanup.Retention - TimeSpan.FromMinutes(1)),
                    RunWorld.Token));

    private static async Task<(Guid SpaceId, Guid UserId, Guid ThreadId)> SeedPrivateSpace(RunWorld world)
    {
        var spaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        return await world.TransactAs(
            spaceId,
            userId,
            async services =>
            {
                var db = services.GetRequiredService<DroverDbContext>();
                db.Users.Add(new User
                {
                    Id = userId,
                    UserName = $"user-{userId:N}",
                    NormalizedUserName = $"USER-{userId:N}".ToUpperInvariant(),
                    Email = $"{userId:N}@example.test",
                    NormalizedEmail = $"{userId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                    DisplayName = "Second user",
                });
                db.Spaces.Add(new Space
                {
                    Id = spaceId,
                    Kind = SpaceKind.Personal,
                    Name = "Second personal space",
                    Slug = $"personal-{spaceId:N}",
                });
                db.Memberships.Add(new Membership { UserId = userId, Role = MembershipRole.Owner });
                await db.SaveChangesAsync(RunWorld.Token);

                var connection = await services.GetRequiredService<ModelConnectionService>()
                    .Create(
                        ScriptedModelProvider.ConnectionKind,
                        "Scripted",
                        null,
                        RunWorld.NewModelKey(),
                        "scripted",
                        RunWorld.Token);
                connection.Problem.Should().BeNull();
                db.ModelCatalog.Add(new ModelCatalogEntry
                {
                    ModelConnectionId = connection.Connection!.Id,
                    Model = "scripted",
                    DisplayName = "Scripted",
                });
                await db.SaveChangesAsync(RunWorld.Token);

                var bot = await services.GetRequiredService<BotService>().Create(
                    new NewBot("Helper", string.Empty, "Answer briefly.", connection.Connection.Id, null, null),
                    AuditActor.Person(userId),
                    RunWorld.Token);
                bot.Problem.Should().BeNull();
                await db.SaveChangesAsync(RunWorld.Token);
                var thread = await services.GetRequiredService<ThreadService>()
                    .Create(bot.Bot!.Id, null, userId, RunWorld.Token);
                thread.Should().NotBeNull();
                return (spaceId, userId, thread!.Id);
            });
    }

    private static async Task<FileUpload> UploadAs(
        RunWorld world,
        Guid spaceId,
        Guid userId,
        Guid threadId,
        string name,
        byte[] content)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var stream = new MemoryStream(content);
        return await scope.ServiceProvider.GetRequiredService<FileService>()
            .Upload(threadId, name, stream, content.Length, userId, RunWorld.Token);
    }

    private static Task<Project> CreateProject(RunWorld world) =>
        world.Transact(async services =>
        {
            var result = await services.GetRequiredService<ProjectService>()
                .Create("Kept", string.Empty, AuditActor.Person(world.UserId), RunWorld.Token);
            result.Project.Should().NotBeNull(result.Problem);
            return result.Project!;
        });

    private static async Task<FileUpload> UploadProjectFile(RunWorld world, Guid projectId, string name, byte[] bytes)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        await using var content = new MemoryStream(bytes);
        return await scope.ServiceProvider.GetRequiredService<FileService>()
            .UploadForProject(projectId, name, content, bytes.Length, world.UserId, RunWorld.Token);
    }

    #endregion

    #region Nested Types

    private sealed class InterruptingStore : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        #endregion

        #region Properties

        public int CancellationsRemaining { get; set; }

        public int Count => files.Count;

        #endregion

        #region Interface Implementations

        public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            files[storageKey] = copy.ToArray();
        }

        public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            return Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);
        }

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);

            if (CancellationsRemaining > 0)
            {
                CancellationsRemaining--;
                throw new OperationCanceledException("Simulated storage timeout.");
            }

            files.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion
}
