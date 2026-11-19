namespace Drover.Engine.Tests.Files;

/// <summary>
/// Shared project files in the engine: storage quota accounting, partial failure cleanup, idempotent and concurrent
/// removal, cleanup on project removal without deleting conversations, and zero sandbox copies.
/// </summary>
public sealed class ProjectFileTests(PostgresServer postgres)
{
    #region Upload, Listing, Download, Quota

    [Fact]
    public async Task Uploading_listing_and_opening_project_files_counts_quota_and_touches_no_sandbox()
    {
        var sandbox = new FakeSandboxProvider();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<ISandboxProvider>(sandbox);
            services.AddSandboxTools();
        });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            return true;
        });

        var project = await CreateProject(world, "Docs");
        var archiveBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4 };
        var textBytes = Encoding.UTF8.GetBytes("shared specification");
        var pngBytes = TestPng.Make(12, 8);

        var first = await UploadProjectFile(world, project.Id, "bundle.zip", archiveBytes);
        var second = await UploadProjectFile(world, project.Id, "spec.txt", textBytes);
        var third = await UploadProjectFile(world, project.Id, "diagram.png", pngBytes);

        first.File.Should().NotBeNull(first.Detail);
        second.File.Should().NotBeNull(second.Detail);
        third.File.Should().NotBeNull(third.Detail);
        third.File!.Width.Should().Be(12);
        third.File.Height.Should().Be(8);

        var listed = await ListProjectFiles(world, project.Id);
        listed.Should().NotBeNull();
        listed!.Select(file => file.Id).Should().Equal(third.File.Id, second.File!.Id, first.File!.Id);

        var downloaded = await OpenProjectFile(world, project.Id, second.File.Id);
        downloaded.Should().NotBeNull();
        await using var downloadedContent = downloaded!.Content;
        using var copy = new MemoryStream();
        await downloadedContent.CopyToAsync(copy, RunWorld.Token);
        copy.ToArray().Should().Equal(textBytes);

        var expectedTotal = archiveBytes.Length + textBytes.Length + pngBytes.Length;
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(expectedTotal);
        sandbox.Created.Should().Be(0, "uploading, listing, or downloading project files never touches a sandbox");
    }

    #endregion

    #region Failure Cleanup And Quotas

    [Fact]
    public async Task A_partial_upload_or_failed_transaction_leaves_no_row_bytes_or_reserved_quota()
    {
        var recordingStore = new RecordingPartialFailureStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(recordingStore));
        var project = await CreateProject(world, "Cleanup");

        // 1. Stream fails partway after bytes have been written to the backing store.
        await using var failingStream = new FailingAfterBytesStream([1, 2, 3, 4, 5, 6, 7, 8], failAfter: 4);
        Func<Task> failedStreamUpload = () => UploadProjectFile(world, project.Id, "partial.bin", failingStream, declaredLength: 8);
        await failedStreamUpload.Should().ThrowAsync<IOException>();

        // 2. Project is removed before KeepForProject commits: store must be cleaned up and quota untouched.
        await RemoveProject(world, project.Id);
        var missingProjectUpload = await UploadProjectFile(world, project.Id, "late.txt", "abc"u8.ToArray());
        missingProjectUpload.Problem.Should().Be(FileProblem.NotFound);

        // 3. Store fails after buffering bytes.
        var project2 = await CreateProject(world, "Second");
        recordingStore.FailNextSave = true;
        Func<Task> failedStoreUpload = () => UploadProjectFile(world, project2.Id, "store-fail.txt", "hello world"u8.ToArray());
        await failedStoreUpload.Should().ThrowAsync<IOException>();

        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(0);
        recordingStore.Count.Should().Be(0, "every partial or uncommitted store entry must be deleted");
    }

    [Fact]
    public async Task Removing_a_project_file_returns_its_quota_exactly_once_across_concurrent_calls_and_retries()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var project = await CreateProject(world, "Concurrent Remove");
        var kept = (await UploadProjectFile(world, project.Id, "kept.txt", new byte[500])).File!;
        var target = (await UploadProjectFile(world, project.Id, "target.txt", new byte[300])).File!;

        var results = await Task.WhenAll(
            RemoveProjectFile(world, project.Id, target.Id),
            RemoveProjectFile(world, project.Id, target.Id));
        var retry = await RemoveProjectFile(world, project.Id, target.Id);

        results.Count(removed => removed).Should().Be(1);
        results.Count(removed => !removed).Should().Be(1);
        retry.Should().BeFalse();
        world.Files.Count.Should().Be(1);
        (await world.Read(db => db.Files.Select(file => file.Id).ToListAsync(RunWorld.Token))).Should().Equal([kept.Id]);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(500);
    }

    [Fact]
    public async Task Removing_a_project_deletes_its_files_and_quota_while_preserving_conversations_and_other_projects()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var projectA = await CreateProject(world, "Alpha");
        var projectB = await CreateProject(world, "Beta");

        var projectAFile = (await UploadProjectFile(world, projectA.Id, "alpha.txt", new byte[200])).File!;
        var projectBFile = (await UploadProjectFile(world, projectB.Id, "beta.txt", new byte[70])).File!;

        var threadId = await world.NewThread();
        await world.Read(db => db.Threads
            .Where(thread => thread.Id == threadId)
            .ExecuteUpdateAsync(set => set.SetProperty(thread => thread.ProjectId, projectA.Id), RunWorld.Token));
        var threadFile = (await world.Upload(threadId, "chat.txt", Encoding.UTF8.GetBytes(new string('c', 50)))).File!;

        (await RemoveProject(world, projectA.Id)).Should().BeTrue();
        (await RemoveProject(world, projectA.Id)).Should().BeFalse("retrying removal does not release quota again");

        var survivingThread = await world.Read(db => db.Threads.SingleAsync(thread => thread.Id == threadId, RunWorld.Token));
        var remainingFiles = await world.Read(db => db.Files.Select(file => file.Id).ToListAsync(RunWorld.Token));
        var storedBytes = await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token));

        survivingThread.ProjectId.Should().BeNull();
        remainingFiles.Should().BeEquivalentTo([threadFile.Id, projectBFile.Id]);
        remainingFiles.Should().NotContain(projectAFile.Id);
        world.Files.Count.Should().Be(2);
        storedBytes.Should().Be(120);
    }

    [Fact]
    public async Task Overlapping_project_uploads_reserve_the_same_quota_before_either_writes()
    {
        var gate = new GatedStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<IFileStore>(gate);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 1 },
            }));
        });
        var project = await CreateProject(world, "Quota");
        var limit = 1024L * 1024L;
        var payload = new byte[1000];
        await world.Transact(async services =>
        {
            var reserved = await services.GetRequiredService<FileRepository>()
                .ReserveBytes(world.SpaceId, limit - payload.Length, limit, RunWorld.Token);
            reserved.Should().BeTrue();
            return true;
        });

        var first = UploadProjectFile(world, project.Id, "one.bin", payload);
        await gate.FirstEntered.WaitAsync(RunWorld.Token);
        var secondTask = UploadProjectFile(world, project.Id, "two.bin", payload);
        var finished = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(15), RunWorld.Token));
        finished.Should().Be(secondTask, "the second upload is refused before it writes");
        var second = await secondTask;
        second.Problem.Should().Be(FileProblem.Refused);
        second.Refusal.Should().Be(FileRefusals.SpaceFull);
        gate.Saves.Should().Be(1);

        gate.Release();
        var kept = await first;
        kept.File.Should().NotBeNull(kept.Detail);
        gate.Saves.Should().Be(1);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(limit);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(1);
    }

    [Fact]
    public async Task A_storage_delete_failure_after_commit_is_finished_by_a_retry()
    {
        var store = new DeleteFailingStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(store));
        var project = await CreateProject(world, "Retry");
        var kept = (await UploadProjectFile(world, project.Id, "kept.txt", new byte[40])).File!;
        var target = (await UploadProjectFile(world, project.Id, "target.txt", new byte[15])).File!;
        store.FailuresRemaining = 1;

        var failed = () => RemoveProjectFile(world, project.Id, target.Id);
        await failed.Should().ThrowAsync<IOException>();

        (await world.Read(db => db.Files.Select(file => file.Id).ToListAsync(RunWorld.Token))).Should().Equal([kept.Id]);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(40);
        (await world.Read(db => db.PendingFileDeletions.CountAsync(RunWorld.Token))).Should().Be(1);
        store.Count.Should().Be(2);

        (await RemoveProjectFile(world, project.Id, target.Id)).Should().BeTrue();
        (await RemoveProjectFile(world, project.Id, target.Id)).Should().BeFalse();
        store.Count.Should().Be(1);
        (await world.Read(db => db.PendingFileDeletions.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(40);
    }

    [Fact]
    public async Task Removing_a_project_finishes_pending_deletions_when_storage_delete_fails()
    {
        var store = new DeleteFailingStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(store));
        var project = await CreateProject(world, "Gone");
        var other = await CreateProject(world, "Stays");
        await UploadProjectFile(world, project.Id, "one.txt", new byte[20]);
        await UploadProjectFile(world, project.Id, "two.txt", new byte[30]);
        await UploadProjectFile(world, other.Id, "other.txt", new byte[11]);
        store.FailuresRemaining = 1;

        var failed = () => RemoveProject(world, project.Id);
        await failed.Should().ThrowAsync<IOException>();

        (await world.Read(db => db.Projects.CountAsync(found => found.Id == project.Id, RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.PendingFileDeletions.CountAsync(RunWorld.Token))).Should().Be(1);
        store.Count.Should().Be(2, "one project file's bytes remain, plus the other project's file");
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(11);

        (await RemoveProject(world, project.Id)).Should().BeTrue();
        (await RemoveProject(world, project.Id)).Should().BeFalse();
        store.Count.Should().Be(1);
        (await world.Read(db => db.PendingFileDeletions.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(11);
    }

    #endregion

    #region Private Helpers

    private static Task<Project> CreateProject(RunWorld world, string name) =>
        world.Transact(async services =>
        {
            var result = await services.GetRequiredService<ProjectService>()
                .Create(name, string.Empty, AuditActor.Person(world.UserId), RunWorld.Token);
            result.Project.Should().NotBeNull(result.Problem);
            return result.Project!;
        });

    private static async Task<bool> RemoveProject(RunWorld world, Guid projectId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<ProjectService>()
            .Remove(projectId, AuditActor.Person(world.UserId), RunWorld.Token);
    }

    private static async Task<FileUpload> UploadProjectFile(RunWorld world, Guid projectId, string name, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await UploadProjectFile(world, projectId, name, stream, bytes.Length);
    }

    private static async Task<FileUpload> UploadProjectFile(RunWorld world, Guid projectId, string name, Stream content, long? declaredLength)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>()
            .UploadForProject(projectId, name, content, declaredLength, world.UserId, RunWorld.Token);
    }

    private static Task<IReadOnlyList<StoredFile>?> ListProjectFiles(RunWorld world, Guid projectId) =>
        world.Transact(services => services.GetRequiredService<FileService>().ListProjectFiles(projectId, RunWorld.Token));

    private static async Task<FileDownload?> OpenProjectFile(RunWorld world, Guid projectId, Guid fileId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>().OpenForProject(projectId, fileId, RunWorld.Token);
    }

    private static async Task<bool> RemoveProjectFile(RunWorld world, Guid projectId, Guid fileId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>().RemoveProjectFile(projectId, fileId, RunWorld.Token);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Simulates a store that leaves partial bytes behind on a failed save so tests prove FileService calls Delete.
    /// </summary>
    private sealed class RecordingPartialFailureStore : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        #endregion

        #region Properties

        public int Count => files.Count;

        public bool FailNextSave { get; set; }

        #endregion

        #region Interface Implementations

        public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            files[storageKey] = copy.ToArray();

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated failure after writing bytes.");
            }
        }

        public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            return Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            files.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        #endregion
    }

    private sealed class FailingAfterBytesStream(byte[] payload, int failAfter) : Stream
    {
        #region Private Fields

        private int position;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => payload.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= failAfter)
            {
                throw new IOException("Simulated stream disconnect mid-upload.");
            }

            var remainingBeforeFail = Math.Min(count, failAfter - position);
            Array.Copy(payload, position, buffer, offset, remainingBeforeFail);
            position += remainingBeforeFail;
            return remainingBeforeFail;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= failAfter)
            {
                throw new IOException("Simulated stream disconnect mid-upload.");
            }

            var remainingBeforeFail = Math.Min(buffer.Length, failAfter - position);
            payload.AsSpan(position, remainingBeforeFail).CopyTo(buffer.Span);
            position += remainingBeforeFail;
            return ValueTask.FromResult(remainingBeforeFail);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    /// <summary>Holds the first save open so a second upload can be observed while those bytes are still uncommitted as a file.</summary>
    private sealed class GatedStore : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int saves;

        #endregion

        #region Properties

        public int Saves => saves;

        public Task FirstEntered => entered.Task;

        #endregion

        #region Public Methods

        public void Release() => release.TrySetResult();

        #endregion

        #region Interface Implementations

        public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            var count = Interlocked.Increment(ref saves);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            files[storageKey] = copy.ToArray();

            if (count == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        }

        public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            return Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            files.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>Fails the next <see cref="FailuresRemaining"/> deletes after the bytes are stored.</summary>
    private sealed class DeleteFailingStore : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        #endregion

        #region Properties

        public int Count => files.Count;

        public int FailuresRemaining { get; set; }

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
            return Task.FromResult<Stream?>(files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new IOException("Simulated storage delete failure.");
            }

            files.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion
}
