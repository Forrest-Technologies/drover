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

    [Fact]
    public async Task Cancelling_a_project_upload_after_reservation_commits_releases_the_hold_and_bytes()
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
        var project = await CreateProject(world, "Cancelled");
        var limit = 1024L * 1024L;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(RunWorld.Token);

        var uploadTask = UploadProjectFile(world, project.Id, "aborted.bin", new MemoryStream(new byte[limit]), limit, cts.Token);

        await gate.FirstEntered.WaitAsync(RunWorld.Token);
        await cts.CancelAsync();
        gate.Release();

        var aborted = () => uploadTask;
        await aborted.Should().ThrowAsync<OperationCanceledException>();

        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(0);

        var retry = await UploadProjectFile(world, project.Id, "after.bin", new byte[limit]);
        retry.File.Should().NotBeNull(retry.Detail);
    }

    [Fact]
    public async Task A_storage_delete_failure_during_aborted_project_upload_still_releases_the_hold()
    {
        var store = new SaveAndDeleteFailingStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(store));
        var project = await CreateProject(world, "DeleteFails");

        var attempt = () => UploadProjectFile(world, project.Id, "broken.bin", new byte[256]);
        await attempt.Should().ThrowAsync<IOException>();

        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(0);
    }

    [Fact]
    public async Task A_member_can_attach_a_file_while_an_expired_project_reservation_awaits_admin_cleanup()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<TimeProvider>(clock));
        var project = await CreateProject(world, "Abandoned");
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.TransactAs(world.SpaceId, member, async services =>
            (await services.GetRequiredService<ThreadService>()
                .Create(world.BotId, null, member, RunWorld.Token))!.Id);
        await world.Transact(async services =>
        {
            var hold = await services.GetRequiredService<FileRepository>().TryHold(
                world.SpaceId,
                Guid.CreateVersion7(),
                64,
                1024 * 1024,
                clock.GetUtcNow() - FileService.HoldLifetime - TimeSpan.FromMinutes(1),
                RunWorld.Token);
            hold.Should().NotBeNull();
            return true;
        });

        await using var content = new MemoryStream("notes"u8.ToArray());
        var upload = await world.WithServiceAs<FileService, FileUpload>(member, service =>
            service.Upload(threadId, "notes.txt", content, 5, member, RunWorld.Token));

        upload.File.Should().NotBeNull(upload.Detail);
        upload.File!.ThreadId.Should().Be(threadId);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(1);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId)
            .Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token))).Should().Be(69);

        var ownerUpload = await UploadProjectFile(world, project.Id, "shared.txt", "shared"u8.ToArray());
        ownerUpload.File.Should().NotBeNull(ownerUpload.Detail);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId)
            .Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token))).Should().Be(11);
    }

    [Fact]
    public async Task An_abandoned_reservation_past_hold_lifetime_is_reclaimed_by_a_later_upload()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 1 },
            }));
        });
        var project = await CreateProject(world, "Abandoned");
        var limit = 1024L * 1024L;
        var abandonedId = Guid.CreateVersion7();

        await world.Transact(async services =>
        {
            var hold = await services.GetRequiredService<FileRepository>()
                .TryHold(world.SpaceId, abandonedId, limit, limit, clock.GetUtcNow(), RunWorld.Token);
            hold.Should().NotBeNull();
            return true;
        });

        var whileFresh = await UploadProjectFile(world, project.Id, "blocked.bin", new byte[1024]);
        whileFresh.Problem.Should().Be(FileProblem.Refused);
        whileFresh.Refusal.Should().Be(FileRefusals.SpaceFull);

        clock.Advance(FileService.HoldLifetime + TimeSpan.FromMinutes(1));

        var afterExpiry = await UploadProjectFile(world, project.Id, "reclaimed.bin", new byte[limit]);
        afterExpiry.File.Should().NotBeNull(afterExpiry.Detail);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(limit);
    }

    [Fact]
    public async Task Two_overlapping_chunked_project_uploads_share_the_space_quota_concurrently()
    {
        var gate = new GatedStore();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<IFileStore>(gate);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 3 },
            }));
        });
        var project = await CreateProject(world, "Chunked");
        var firstSize = 1_200_000;
        var secondSize = 700 * 1024;

        await using var firstStream = new MemoryStream(new byte[firstSize]);
        var firstUpload = UploadProjectFile(world, project.Id, "chunked-one.bin", firstStream, declaredLength: null, cancellationToken: TestContext.Current.CancellationToken);
        await gate.FirstEntered.WaitAsync(RunWorld.Token);

        await using var secondStream = new MemoryStream(new byte[secondSize]);
        var secondUpload = await UploadProjectFile(world, project.Id, "chunked-two.bin", secondStream, declaredLength: null, cancellationToken: TestContext.Current.CancellationToken);
        secondUpload.File.Should().NotBeNull(secondUpload.Detail);

        gate.Release();
        var completedFirst = await firstUpload;
        completedFirst.File.Should().NotBeNull(completedFirst.Detail);

        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(firstSize + secondSize);
    }

    [Fact]
    public async Task A_chunked_upload_that_keeps_extending_survives_past_hold_lifetime_and_an_expired_hold_reports_why()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var stepped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStore = new CoordinatedStore();
        slowStore.Arm(stepped, finish);

        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IFileStore>(slowStore);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 4 },
            }));
        });
        var project = await CreateProject(world, "SlowButActive");
        var totalSize = (int)FileService.ChunkHoldStepBytes + (256 * 1024);

        // Advance 10 minutes before the upload crosses the step boundary, so ExtendHold renews at +10m;
        // advance another 10 minutes after extending (20m total > 15m HoldLifetime, but only 10m since touch).
        var uploadStream = new SlowStepStream(new byte[totalSize], (int)FileService.ChunkHoldStepBytes, clock, TimeSpan.FromMinutes(10));
        var upload = UploadProjectFile(world, project.Id, "steady.bin", uploadStream, declaredLength: null, cancellationToken: RunWorld.Token);
        await stepped.Task.WaitAsync(RunWorld.Token);

        clock.Advance(TimeSpan.FromMinutes(10));
        var concurrent = await UploadProjectFile(world, project.Id, "other.bin", new byte[128]);
        concurrent.File.Should().NotBeNull(concurrent.Detail);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(1, "renewed hold must not be reaped");

        finish.SetResult();
        var completed = await upload;
        completed.File.Should().NotBeNull(completed.Detail);

        // Now stall past HoldLifetime without renewing so the reaper drops the hold before KeepForProject commits.
        var stalledEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalledFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        slowStore.Arm(stalledEntered, stalledFinish);

        var stalledUpload = UploadProjectFile(world, project.Id, "stalled.bin", new MemoryStream(new byte[1024]), declaredLength: 1024, cancellationToken: RunWorld.Token);
        await stalledEntered.Task.WaitAsync(RunWorld.Token);
        clock.Advance(FileService.HoldLifetime + TimeSpan.FromMinutes(1));
        var reaper = await UploadProjectFile(world, project.Id, "reaper.bin", new byte[64]);
        reaper.File.Should().NotBeNull(reaper.Detail);

        stalledFinish.SetResult();
        var expired = await stalledUpload;
        expired.File.Should().BeNull();
        expired.Problem.Should().Be(FileProblem.Refused);
        expired.Refusal.Should().Be(FileRefusals.UploadExpired);
        expired.Detail.Should().Contain("expired");
    }

    [Fact]
    public async Task A_known_length_upload_that_keeps_streaming_renews_its_hold_past_hold_lifetime()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var stepped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStore = new CoordinatedStore();
        slowStore.Arm(stepped, finish);

        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IFileStore>(slowStore);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 4 },
            }));
        });
        var project = await CreateProject(world, "SlowKnownLength");
        var payload = new byte[4096];

        // Advance 10 minutes at byte 2048 so RenewHold updates created_at at +10m; then advance another 10 minutes
        // after Save finishes reading (20m total > 15m HoldLifetime, 10m since touch) while a concurrent upload sweeps.
        var uploadStream = new SlowStepStream(payload, 2048, clock, TimeSpan.FromMinutes(10));
        var upload = UploadProjectFile(world, project.Id, "declared-slow.bin", uploadStream, declaredLength: payload.Length, cancellationToken: RunWorld.Token);
        await stepped.Task.WaitAsync(RunWorld.Token);

        clock.Advance(TimeSpan.FromMinutes(10));
        var concurrent = await UploadProjectFile(world, project.Id, "other.bin", new byte[128]);
        concurrent.File.Should().NotBeNull(concurrent.Detail);
        (await world.Read(db => db.FileByteReservations.CountAsync(RunWorld.Token))).Should().Be(1, "renewed known-length hold must not be reaped");

        finish.SetResult();
        var completed = await upload;
        completed.File.Should().NotBeNull(completed.Detail);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(payload.Length + 128);
    }

    [Fact]
    public async Task A_known_length_upload_stops_mid_stream_when_its_hold_already_expired()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<TimeProvider>(clock));
        var project = await CreateProject(world, "ReapedMidStream");
        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new GatedAfterFirstChunkStream(new byte[4096], 1024, gateEntered, gateContinue);

        var stalled = UploadProjectFile(world, project.Id, "stalled-declared.bin", stream, declaredLength: 4096, cancellationToken: RunWorld.Token);
        await gateEntered.Task.WaitAsync(RunWorld.Token);

        clock.Advance(FileService.HoldLifetime + TimeSpan.FromMinutes(1));
        var reaper = await UploadProjectFile(world, project.Id, "reaper.bin", new byte[64]);
        reaper.File.Should().NotBeNull(reaper.Detail);

        gateContinue.SetResult();
        var result = await stalled;
        result.File.Should().BeNull();
        result.Problem.Should().Be(FileProblem.Refused);
        result.Refusal.Should().Be(FileRefusals.UploadExpired);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(64);
    }

    [Fact]
    public async Task Cancelling_the_request_during_project_file_deletion_still_finishes_pending_cleanup()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(RunWorld.Token);
        var cancellingStore = new CancellingOnDeleteStore(cts);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IFileStore>(cancellingStore));
        var project = await CreateProject(world, "CancelledDelete");
        var stored = (await UploadProjectFile(world, project.Id, "drop.txt", new byte[120])).File!;

        cancellingStore.FailNextDelete = true;

        await using (var scope = world.Hosts[0].Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
            var failing = () => scope.ServiceProvider.GetRequiredService<FileService>()
                .RemoveProjectFile(project.Id, stored.Id, cts.Token);
            await failing.Should().ThrowAsync<IOException>();
        }

        // The claim must have been cleared even though the request token was cancelled before ClearClaim ran.
        (await world.Read(db => db.PendingFileDeletions.SingleAsync(row => row.Id == stored.Id, RunWorld.Token)))
            .CleanupClaimUntil.Should().BeNull();

        using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(RunWorld.Token);
        cancellingStore.Reset(retryCts);
        await using (var scope = world.Hosts[0].Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
            var retried = await scope.ServiceProvider.GetRequiredService<FileService>()
                .RemoveProjectFile(project.Id, stored.Id, retryCts.Token);
            retried.Should().BeTrue();
        }

        (await world.Read(db => db.PendingFileDeletions.CountAsync(RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(0);
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

    private static Task<FileUpload> UploadProjectFile(RunWorld world, Guid projectId, string name, Stream content, long? declaredLength) =>
        UploadProjectFile(world, projectId, name, content, declaredLength, RunWorld.Token);

    private static async Task<FileUpload> UploadProjectFile(
        RunWorld world,
        Guid projectId,
        string name,
        Stream content,
        long? declaredLength,
        CancellationToken cancellationToken)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>()
            .UploadForProject(projectId, name, content, declaredLength, world.UserId, cancellationToken);
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

    private sealed class SaveAndDeleteFailingStore : IFileStore
    {
        #region Interface Implementations

        public Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken) =>
            throw new IOException("Simulated upload failure.");

        public Task<Stream?> Open(Guid spaceId, string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken) =>
            throw new IOException("Simulated delete failure during cleanup.");

        #endregion
    }

    private sealed class CancellingOnDeleteStore(CancellationTokenSource requestCts) : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        private CancellationTokenSource activeCts = requestCts;

        #endregion

        #region Properties

        public bool FailNextDelete { get; set; }

        #endregion

        #region Public Methods

        public void Reset(CancellationTokenSource cts)
        {
            activeCts = cts;
            FailNextDelete = false;
        }

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

        public async Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            await activeCts.CancelAsync();

            if (FailNextDelete)
            {
                FailNextDelete = false;
                throw new IOException("Simulated failure after cancellation.");
            }

            files.TryRemove(storageKey, out _);
        }

        #endregion
    }

    private sealed class CoordinatedStore : IFileStore
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);

        private TaskCompletionSource? activeEntered;

        private TaskCompletionSource? activeRelease;

        #endregion

        #region Public Methods

        public void Arm(TaskCompletionSource nextEntered, TaskCompletionSource nextRelease)
        {
            activeEntered = nextEntered;
            activeRelease = nextRelease;
        }

        #endregion

        #region Interface Implementations

        public async Task Save(Guid spaceId, string storageKey, Stream content, CancellationToken cancellationToken)
        {
            FileStorageKeys.Require(spaceId, storageKey);
            var entered = Interlocked.Exchange(ref activeEntered, null);
            var release = Interlocked.Exchange(ref activeRelease, null);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            files[storageKey] = copy.ToArray();

            if (entered is not null && release is not null)
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

    private sealed class SlowStepStream(byte[] content, int advanceAtByte, MovableClock clock, TimeSpan advanceBy) : Stream
    {
        #region Private Fields

        private int position;

        private bool advanced;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = content.Length - position;
            if (remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            if (!advanced && position >= advanceAtByte)
            {
                advanced = true;
                clock.Advance(advanceBy);
            }

            var cap = !advanced ? Math.Max(1, Math.Min(buffer.Length, advanceAtByte - position)) : buffer.Length;
            var toCopy = Math.Min(remaining, cap);
            content.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    private sealed class GatedAfterFirstChunkStream(
        byte[] content,
        int firstChunk,
        TaskCompletionSource entered,
        TaskCompletionSource release) : Stream
    {
        #region Private Fields

        private int position;

        private bool gated;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= content.Length)
            {
                return 0;
            }

            if (!gated && position >= firstChunk)
            {
                gated = true;
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            var cap = !gated ? Math.Min(buffer.Length, firstChunk - position) : buffer.Length;
            var toCopy = Math.Min(content.Length - position, cap);
            content.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return toCopy;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        #region Private Fields

        private DateTimeOffset current = start;

        #endregion

        #region Public Methods

        public void Advance(TimeSpan delta) => current = current.Add(delta);

        #endregion

        #region Overrides

        public override DateTimeOffset GetUtcNow() => current;

        #endregion
    }

    #endregion
}
