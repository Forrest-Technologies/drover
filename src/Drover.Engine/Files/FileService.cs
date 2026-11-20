namespace Drover.Engine.Files;

public enum FileProblem
{
    None,
    NotFound,
    Refused,
}

/// <summary>What an upload came to: the stored file, or why there is none, as a refusal code and a sentence.</summary>
public sealed record FileUpload(StoredFile? File, FileProblem Problem = FileProblem.None, string? Refusal = null, string? Detail = null);

/// <summary>A file the caller may read, and its content, which the caller disposes.</summary>
public sealed record FileDownload(StoredFile File, Stream Content);

/// <summary>
/// Files a person attaches to a message. Who may read a file is decided by its row, read inside a tenant scope where
/// row-level security shows a person only the files of conversations they can see; a storage key is only ever used
/// after that, and only with the space the scope is for. Uploading and downloading wait on the network, so each
/// opens short scopes of its own and holds none while bytes move. Reading a thread's rules runs in the scope the
/// endpoint filter opened.
/// </summary>
public sealed class FileService(
    TenantScope scope,
    TenantContext tenant,
    FileRepository files,
    ThreadRepository threads,
    ProjectRepository projects,
    BotRepository bots,
    FileAbilityReader abilities,
    IFileStore store,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<FileService> logger)
{
    #region Private Fields

    private const int MaxNameLength = 200;

    /// <summary>The most files a list of files exchanged with a bot holds: the newest ones.</summary>
    public const int MaxExchangedFiles = 200;

    /// <summary>How much quota a chunked project upload with no declared length holds at a time.</summary>
    public const long ChunkHoldStepBytes = 1024 * 1024;

    /// <summary>How long an uncommitted project-upload byte reservation stays valid before a later request reclaims it.</summary>
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromMinutes(15);

    /// <summary>How often an active project upload renews its byte reservation while streaming.</summary>
    public static readonly TimeSpan HoldRenewInterval = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan cleanupTimeout = TimeSpan.FromSeconds(15);

    #endregion

    #region Properties

    private DeploymentPolicy Rules => deployment.Value.Rules;

    private long MaxSpaceBytes => Rules.MaxSpaceFileMegabytes * 1024L * 1024;

    private Guid SpaceId => tenant.SpaceId ?? throw new InvalidOperationException("Files are always handled inside a space.");

    #endregion

    #region Public Methods

    /// <summary>What the thread's bot can take, or null when the caller cannot see the thread.</summary>
    public async Task<AttachmentRules?> GetRules(Guid threadId, CancellationToken cancellationToken)
    {
        if (await threads.FindThread(threadId, cancellationToken) is not { } thread
            || await bots.FindBot(thread.BotId, cancellationToken) is not { } bot)
        {
            return null;
        }

        var can = await abilities.Read(bot, thread, cancellationToken);
        return new AttachmentRules(Rules.MaxFileBytes, Rules.MaxFilesPerMessage, can.ModelReadsImages, can is { HasSandbox: true, AudienceCoversSandbox: true });
    }

    /// <summary>
    /// Stores a file for a message the person has not sent yet. The kind is decided from the bytes, and a file the
    /// conversation's bot has no way to open is refused here, before anything is sent, as is one past a limit.
    /// </summary>
    public async Task<FileUpload> Upload(
        Guid threadId,
        string? name,
        Stream content,
        long? declaredLength,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (declaredLength > Rules.MaxFileBytes)
        {
            return Refused(FileRefusals.TooLarge, FileWords.TooLarge(declaredLength, Rules.MaxFileBytes));
        }

        if (await Prepare(threadId, declaredLength ?? 0, cancellationToken) is not { } prepared)
        {
            return new FileUpload(null, FileProblem.NotFound);
        }

        if (prepared.Refusal is not null)
        {
            return prepared.Refusal;
        }

        var clean = CleanName(name);
        var id = Guid.CreateVersion7();
        var key = FileStorageKeys.For(SpaceId, id);
        var inspector = new FileInspector();
        // A body with no declared length is cut off at the space's room as well as at the file limit, so it never
        // fills the disk past what the space may hold. Keep reserves the bytes for good once the size is known.
        var limit = Math.Min(Rules.MaxFileBytes, prepared.Room);
        await using var metered = new MeteredStream(content, limit, inspector);

        try
        {
            await store.Save(SpaceId, key, metered, cancellationToken);
        }
        catch (FileTooLargeException tooLarge)
        {
            await store.Delete(SpaceId, key, CancellationToken.None);
            logger.LogInformation(tooLarge, "An upload to thread {ThreadId} went past {MaxBytes} bytes and was refused", threadId, limit);
            return limit < Rules.MaxFileBytes
                ? Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes))
                : Refused(FileRefusals.TooLarge, FileWords.TooLarge(null, Rules.MaxFileBytes));
        }
        catch (Exception)
        {
            await store.Delete(SpaceId, key, CancellationToken.None);
            throw;
        }

        var facts = inspector.Finish(clean);
        var file = new StoredFile
        {
            Id = id,
            StorageKey = key,
            Name = clean,
            ContentType = facts.ContentType,
            Size = metered.BytesRead,
            Sha256 = metered.FinishHash(),
            CreatedByUserId = userId,
            Width = facts.Width,
            Height = facts.Height,
            Preview = facts.Preview,
            CreatedAt = time.GetUtcNow(),
        };

        FileUpload upload;

        try
        {
            if (facts.Kind is FileKind.Document)
            {
                file.ReadingNote = await ReadingNote(file, prepared.Abilities.ModelReadsImages, cancellationToken);
            }

            upload = FileUses.RefusalFor(facts.Kind, prepared.Abilities) is { } refusal
                ? Refused(refusal, FileWords.CannotOpen(refusal, prepared.BotName))
                : await Keep(threadId, file, cancellationToken);
        }
        catch (Exception)
        {
            await store.Delete(SpaceId, key, CancellationToken.None);
            throw;
        }

        if (upload.File is null)
        {
            await store.Delete(SpaceId, key, CancellationToken.None);
        }

        return upload;
    }

    /// <summary>
    /// Stores a shared file in a project using the space's file size limits and storage quota. Opens short scopes of its
    /// own so no database scope stays open while bytes stream.
    /// </summary>
    public async Task<FileUpload> UploadForProject(
        Guid projectId,
        string? name,
        Stream content,
        long? declaredLength,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (declaredLength > Rules.MaxFileBytes)
        {
            return Refused(FileRefusals.TooLarge, FileWords.TooLarge(declaredLength, Rules.MaxFileBytes));
        }

        if (await PrepareForProject(projectId, declaredLength ?? 0, cancellationToken) is not { } prepared)
        {
            return new FileUpload(null, FileProblem.NotFound);
        }

        if (prepared.Refusal is not null)
        {
            return prepared.Refusal;
        }

        var chunked = declaredLength is not > 0;
        var allowance = chunked
            ? Math.Min(ChunkHoldStepBytes, Math.Min(Rules.MaxFileBytes, prepared.Room))
            : declaredLength!.Value;

        if (allowance <= 0)
        {
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }

        var id = Guid.CreateVersion7();
        var held = await HoldProjectBytes(projectId, id, allowance, cancellationToken);

        if (held.Missing)
        {
            return new FileUpload(null, FileProblem.NotFound);
        }

        if (held.Hold is not { } initialHold)
        {
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }

        var holdBox = new HoldBox(initialHold, time.GetUtcNow());
        var clean = CleanName(name);
        var key = FileStorageKeys.For(SpaceId, id);
        var inspector = new FileInspector();
        var streamLimit = chunked ? Rules.MaxFileBytes : initialHold.Bytes;
        await using var source = new ProjectUploadStream(
            content,
            chunked,
            streamLimit,
            holdBox,
            time,
            ExtendProjectHold,
            RenewProjectHold,
            projectId);
        await using var metered = new MeteredStream(source, streamLimit, inspector);

        try
        {
            await store.Save(SpaceId, key, metered, cancellationToken);
        }
        catch (ProjectUploadExpiredException expired)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            logger.LogInformation(expired, "A chunked upload to project {ProjectId} expired before finishing", projectId);
            return Refused(FileRefusals.UploadExpired, FileWords.UploadExpired(HoldLifetime));
        }
        catch (ProjectQuotaFullException quotaFull)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            logger.LogInformation(quotaFull, "A chunked upload to project {ProjectId} ran out of space quota", projectId);
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }
        catch (ProjectMissingException missing)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            logger.LogInformation(missing, "Project {ProjectId} could not be found during chunked upload", projectId);
            return new FileUpload(null, FileProblem.NotFound);
        }
        catch (FileTooLargeException tooLarge)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            logger.LogInformation(tooLarge, "An upload to project {ProjectId} went past {MaxBytes} bytes and was refused", projectId, streamLimit);
            return streamLimit < Rules.MaxFileBytes
                ? Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes))
                : Refused(FileRefusals.TooLarge, FileWords.TooLarge(null, Rules.MaxFileBytes));
        }
        catch (Exception)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            throw;
        }

        var facts = inspector.Finish(clean);
        var file = new StoredFile
        {
            Id = id,
            StorageKey = key,
            Name = clean,
            ContentType = facts.ContentType,
            Size = metered.BytesRead,
            Sha256 = metered.FinishHash(),
            CreatedByUserId = userId,
            Width = facts.Width,
            Height = facts.Height,
            Preview = facts.Preview,
            CreatedAt = time.GetUtcNow(),
        };

        FileUpload upload;

        try
        {
            if (facts.Kind is FileKind.Document)
            {
                file.ReadingNote = await ReadingNote(file, modelReadsImages: false, cancellationToken);
            }

            upload = await KeepForProject(projectId, file, holdBox.Hold, cancellationToken);
        }
        catch (Exception)
        {
            await CleanupAbortedProjectUpload(key, holdBox.Hold);
            throw;
        }

        if (upload.File is null)
        {
            using var timeout = new CancellationTokenSource(cleanupTimeout);
            await DeleteStoredQuietly(key, timeout.Token);
        }

        return upload;
    }

    /// <summary>
    /// The files shared in a project, newest first. Null when the caller cannot see the project. Runs in the scope the
    /// endpoint filter opened.
    /// </summary>
    public async Task<IReadOnlyList<StoredFile>?> ListProjectFiles(Guid projectId, CancellationToken cancellationToken) =>
        await projects.FindProject(projectId, cancellationToken) is null
            ? null
            : await files.ListProjectFiles(projectId, cancellationToken);

    /// <summary>
    /// The files the person and the bot exchanged in the person's conversations with it, newest first. Null when the
    /// caller cannot see the bot. Runs in the scope the endpoint filter opened.
    /// </summary>
    public async Task<IReadOnlyList<ExchangedFile>?> ListExchanged(Guid botId, Guid userId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is null
            ? null
            : await files.ListExchangedFiles(botId, userId, MaxExchangedFiles, cancellationToken);

    /// <summary>The file and its content, when the caller can see the file's row. The scope is closed before the content is read.</summary>
    public async Task<FileDownload?> Open(Guid fileId, CancellationToken cancellationToken)
    {
        StoredFile? file;

        await using (var work = await scope.Begin(cancellationToken))
        {
            file = await files.FindFile(fileId, cancellationToken);
            await work.Commit(cancellationToken);
        }

        if (file is null)
        {
            return null;
        }

        if (await store.Open(SpaceId, file.StorageKey, cancellationToken) is not { } content)
        {
            logger.LogWarning("File {FileId} has a row and nothing in the file store", fileId);
            return null;
        }

        return new FileDownload(file, content);
    }

    /// <summary>
    /// A shared project file and its content, when it belongs to <paramref name="projectId"/> in this space. The scope
    /// closes before the content stream is opened.
    /// </summary>
    public async Task<FileDownload?> OpenForProject(Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        StoredFile? file;

        await using (var work = await scope.Begin(cancellationToken))
        {
            file = await files.FindProjectFile(projectId, fileId, cancellationToken);
            await work.Commit(cancellationToken);
        }

        if (file is null)
        {
            return null;
        }

        if (await store.Open(SpaceId, file.StorageKey, cancellationToken) is not { } content)
        {
            logger.LogWarning("Project file {FileId} in project {ProjectId} has a row and nothing in the file store", fileId, projectId);
            return null;
        }

        return new FileDownload(file, content);
    }

    /// <summary>
    /// Removes a file the person attached and has not sent, and gives its bytes back to the space. A sent file is
    /// kept as long as its message. False when there is no such file of theirs waiting to be sent.
    /// </summary>
    public async Task<bool> RemovePending(Guid fileId, Guid userId, CancellationToken cancellationToken)
    {
        StoredFile? file;

        await using (var work = await scope.Begin(cancellationToken))
        {
            file = (await files.GetFilesForUpdate([fileId], cancellationToken)).SingleOrDefault();

            if (file is not { ProjectId: null, MessageId: null } || file.CreatedByUserId != userId)
            {
                return false;
            }

            files.Remove(file);
            await files.ReleaseBytes(SpaceId, file.Size, cancellationToken);
            await work.Commit(cancellationToken);
        }

        await store.Delete(SpaceId, file.StorageKey, CancellationToken.None);
        logger.LogInformation("File {FileId} was removed before it was sent", fileId);
        return true;
    }

    /// <summary>
    /// Removes a file shared in a project and returns its bytes to the space once, even across concurrent or retried
    /// requests. False when the file is absent from that project in this space.
    /// </summary>
    public async Task<bool> RemoveProjectFile(Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        StoredDeletion? deletion;
        var now = time.GetUtcNow();
        var claimUntil = now.Add(ProjectFileCleanup.ClaimWindow);

        await using (var work = await scope.Begin(cancellationToken))
        {
            var file = await files.GetProjectFileForUpdate(projectId, fileId, cancellationToken);

            if (file is not null)
            {
                deletion = new StoredDeletion(file.Id);
                await files.StageProjectFileDeletion([file], now, claimUntil, cancellationToken);
            }
            else if (await files.ClaimPending(fileId, projectId, now, claimUntil, cancellationToken) is { } pending)
            {
                deletion = new StoredDeletion(pending.Id);
            }
            else
            {
                return false;
            }

            await work.Commit(cancellationToken);
        }

        await ProjectFileCleanup.Finish(scope, files, store, SpaceId, [deletion.Value], logger, cancellationToken);
        logger.LogInformation("Project file {FileId} removed from project {ProjectId}", fileId, projectId);
        return true;
    }

    /// <summary>A name as a person may see it and a sandbox may hold it: no folders, no control characters, bounded.</summary>
    public static string CleanName(string? name)
    {
        var last = (name ?? string.Empty).Split('/', '\\').LastOrDefault() ?? string.Empty;
        var clean = new string([.. last.Where(character => !char.IsControl(character) && character is not ('<' or '>' or ':' or '"' or '|' or '?' or '*'))]).Trim().Trim('.');
        clean = clean.Length > MaxNameLength ? clean[^MaxNameLength..] : clean;
        return clean.Length == 0 ? "file" : clean;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Reads a document once, within the limits a run reads it with, so the person sees under it when a bot will read
    /// less than all of it. No scope is open: the bytes come back from the file store.
    /// </summary>
    private async Task<string?> ReadingNote(StoredFile file, bool modelReadsImages, CancellationToken cancellationToken)
    {
        await using var content = await store.Open(SpaceId, file.StorageKey, cancellationToken);

        if (content is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);

        try
        {
            var limits = new DocumentLimits(Rules.MaxDocumentPages, RunFiles.MaxTextCharacters, 0);
            var read = DocumentReader.Read(copy.ToArray(), file.ContentType, limits);
            return DocumentWords.ForPerson(read, RunFiles.MaxTextCharacters, modelReadsImages);
        }
        catch (DocumentUnreadableException unreadable)
        {
            // The parser's own message may quote the file, so only the kind of failure is logged.
            logger.LogInformation("Document {FileId} could not be read: {Failure}", file.Id, unreadable.InnerException?.GetType().Name);
            return DocumentWords.Unreadable(unreadable.Message);
        }
    }

    /// <summary>
    /// The thread, its bot, and what the bot can take, read before any byte is; and an early refusal when the space
    /// cannot take a file of the declared length. Null when the caller cannot see the thread.
    /// </summary>
    private async Task<Prepared?> Prepare(Guid threadId, long declaredLength, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await threads.FindThread(threadId, cancellationToken) is not { } thread
            || await bots.FindBot(thread.BotId, cancellationToken) is not { } bot)
        {
            return null;
        }

        var can = await abilities.Read(bot, thread, cancellationToken);
        var stored = await files.GetStoredBytes(SpaceId, cancellationToken);
        await work.Commit(cancellationToken);

        var room = Math.Max(0, MaxSpaceBytes - stored);
        return declaredLength > room
            ? new Prepared(bot.Name, can, room, Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes)))
            : new Prepared(bot.Name, can, room, null);
    }

    private async Task<PreparedProject?> PrepareForProject(Guid projectId, long declaredLength, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await projects.FindProject(projectId, cancellationToken) is null)
        {
            return null;
        }

        var expired = await files.ReleaseExpiredHolds(SpaceId, time.GetUtcNow() - HoldLifetime, cancellationToken);
        var stored = await files.GetStoredBytes(SpaceId, cancellationToken);
        await work.Commit(cancellationToken);
        await DeleteExpiredStaging(expired);

        var room = Math.Max(0, MaxSpaceBytes - stored);
        return declaredLength > room
            ? new PreparedProject(room, Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes)))
            : new PreparedProject(room, null);
    }

    /// <summary>
    /// Stores the row with the thread's audience, as the thread is now, and counts its bytes against the space, in
    /// one transaction. A thread the caller can no longer see takes nothing.
    /// </summary>
    private async Task<FileUpload> Keep(Guid threadId, StoredFile file, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await threads.FindThread(threadId, cancellationToken) is not { } thread)
        {
            return new FileUpload(null, FileProblem.NotFound);
        }

        if (!await files.ReserveBytes(SpaceId, file.Size, MaxSpaceBytes, cancellationToken))
        {
            await work.Commit(cancellationToken);
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }

        file.ThreadId = thread.Id;
        file.AudienceUserId = thread.AudienceUserId;
        files.Add(file);
        await work.Commit(cancellationToken);
        logger.LogInformation("File {FileId} of {Size} bytes stored for thread {ThreadId}", file.Id, file.Size, threadId);
        return new FileUpload(file);
    }

    private async Task<long> ExtendProjectHold(Guid projectId, HoldBox holdBox, long neededBytes, CancellationToken cancellationToken)
    {
        var current = holdBox.Hold;
        var additionalNeeded = neededBytes - current.Bytes;

        if (additionalNeeded <= 0)
        {
            return current.Bytes;
        }

        var stepTarget = Math.Min(Rules.MaxFileBytes, Math.Max(neededBytes, current.Bytes + ChunkHoldStepBytes));
        var desiredStep = stepTarget - current.Bytes;

        await using var work = await scope.Begin(cancellationToken);

        if (await projects.FindProject(projectId, cancellationToken) is null)
        {
            throw new ProjectMissingException();
        }

        var touchedAt = time.GetUtcNow();
        long added;
        var step = await files.ExtendHold(SpaceId, current.Id, desiredStep, MaxSpaceBytes, touchedAt, cancellationToken);

        if (step is HoldExtension.Extended)
        {
            added = desiredStep;
        }
        else if (step is HoldExtension.Expired)
        {
            throw new ProjectUploadExpiredException();
        }
        else if (desiredStep != additionalNeeded)
        {
            var exact = await files.ExtendHold(SpaceId, current.Id, additionalNeeded, MaxSpaceBytes, touchedAt, cancellationToken);
            added = exact switch
            {
                HoldExtension.Extended => additionalNeeded,
                HoldExtension.Expired => throw new ProjectUploadExpiredException(),
                _ => throw new ProjectQuotaFullException(),
            };
        }
        else
        {
            throw new ProjectQuotaFullException();
        }

        await work.Commit(cancellationToken);
        holdBox.Hold = current with { Bytes = current.Bytes + added };
        holdBox.LastTouchedAt = touchedAt;
        return holdBox.Hold.Bytes;
    }

    private async Task RenewProjectHold(Guid projectId, HoldBox holdBox, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await projects.FindProject(projectId, cancellationToken) is null)
        {
            throw new ProjectMissingException();
        }

        var touchedAt = time.GetUtcNow();

        if (!await files.RenewHold(SpaceId, holdBox.Hold.Id, touchedAt, cancellationToken))
        {
            throw new ProjectUploadExpiredException();
        }

        await work.Commit(cancellationToken);
        holdBox.LastTouchedAt = touchedAt;
    }

    private async Task<HoldAttempt> HoldProjectBytes(Guid projectId, Guid fileId, long bytes, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> expired;
        FileByteReservation? reservation;

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await projects.FindProject(projectId, cancellationToken) is null)
            {
                return HoldAttempt.NotFound;
            }

            var now = time.GetUtcNow();
            expired = await files.ReleaseExpiredHolds(SpaceId, now - HoldLifetime, cancellationToken);
            reservation = await files.TryHold(SpaceId, fileId, bytes, MaxSpaceBytes, now, cancellationToken);
            await work.Commit(cancellationToken);
        }

        await DeleteExpiredStaging(expired);

        return reservation is null
            ? HoldAttempt.Full
            : new HoldAttempt(new ByteHold(reservation.Id, reservation.Bytes), false);
    }

    private async Task CleanupAbortedProjectUpload(string storageKey, ByteHold hold)
    {
        using var timeout = new CancellationTokenSource(cleanupTimeout);
        await DeleteStoredQuietly(storageKey, timeout.Token);

        try
        {
            await ReleaseHold(hold, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not release byte reservation {ReservationId} after an aborted upload", hold.Id);
        }
    }

    private async Task DeleteExpiredStaging(IReadOnlyList<Guid> expired)
    {
        if (expired.Count == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(cleanupTimeout);

        foreach (var id in expired)
        {
            await DeleteStoredQuietly(FileStorageKeys.For(SpaceId, id), timeout.Token);
        }
    }

    private async Task DeleteStoredQuietly(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await store.Delete(SpaceId, storageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not delete staged bytes for key {StorageKey}", storageKey);
        }
    }

    private async Task ReleaseHold(ByteHold hold, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        await files.ReleaseHold(SpaceId, hold.Id, hold.Bytes, cancellationToken);
        await work.Commit(cancellationToken);
    }

    private async Task<FileUpload> KeepForProject(Guid projectId, StoredFile file, ByteHold hold, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await projects.LockProjectForShare(projectId, cancellationToken) is null)
        {
            await files.ReleaseHold(SpaceId, hold.Id, hold.Bytes, cancellationToken);
            await work.Commit(cancellationToken);
            return new FileUpload(null, FileProblem.NotFound);
        }

        var completion = await files.CompleteHold(SpaceId, hold.Id, file.Size, cancellationToken);

        if (completion is HoldCompletion.Expired)
        {
            await work.Commit(cancellationToken);
            return Refused(FileRefusals.UploadExpired, FileWords.UploadExpired(HoldLifetime));
        }

        if (completion is HoldCompletion.SpaceFull)
        {
            await work.Commit(cancellationToken);
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }

        file.ProjectId = projectId;
        file.AudienceUserId = Audience.Everyone;
        files.Add(file);
        await work.Commit(cancellationToken);
        logger.LogInformation("File {FileId} of {Size} bytes stored for project {ProjectId}", file.Id, file.Size, projectId);
        return new FileUpload(file);
    }

    private static FileUpload Refused(string refusal, string detail) => new(null, FileProblem.Refused, refusal, detail);

    #endregion

    #region Nested Types

    /// <summary>What an upload needs before its bytes move. <see cref="Room"/> is how many more bytes the space may hold.</summary>
    private sealed record Prepared(string BotName, FileAbilities Abilities, long Room, FileUpload? Refusal);

    private sealed record PreparedProject(long Room, FileUpload? Refusal);

    private readonly record struct ByteHold(Guid Id, long Bytes);

    private sealed record HoldAttempt(ByteHold? Hold, bool Missing)
    {
        public static HoldAttempt NotFound { get; } = new(null, true);

        public static HoldAttempt Full { get; } = new(null, false);
    }

    private sealed class HoldBox(ByteHold hold, DateTimeOffset touchedAt)
    {
        #region Properties

        public ByteHold Hold { get; set; } = hold;

        public DateTimeOffset LastTouchedAt { get; set; } = touchedAt;

        #endregion
    }

    private sealed class ProjectQuotaFullException() : IOException("The space has reached its file storage limit.");

    private sealed class ProjectUploadExpiredException() : IOException("The upload reservation expired before the upload finished.");

    private sealed class ProjectMissingException() : IOException("The project could not be found.");

    private sealed class ProjectUploadStream(
        Stream inner,
        bool chunked,
        long maxFileBytes,
        HoldBox holdBox,
        TimeProvider time,
        Func<Guid, HoldBox, long, CancellationToken, Task<long>> extendHold,
        Func<Guid, HoldBox, CancellationToken, Task> renewHold,
        Guid projectId) : Stream
    {
        #region Private Fields

        private long bytesRead;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            EnsureReservedSync(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);

            if (read <= 0)
            {
                return read;
            }

            bytesRead += read;

            if (bytesRead > maxFileBytes)
            {
                throw new FileTooLargeException();
            }

            if (chunked && bytesRead > holdBox.Hold.Bytes)
            {
                await extendHold(projectId, holdBox, bytesRead, cancellationToken);
            }
            else if (time.GetUtcNow() - holdBox.LastTouchedAt >= HoldRenewInterval)
            {
                await renewHold(projectId, holdBox, cancellationToken);
            }

            return read;
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

        #region Private Methods

        private void EnsureReservedSync(int read)
        {
            if (read <= 0)
            {
                return;
            }

            bytesRead += read;

            if (bytesRead > maxFileBytes)
            {
                throw new FileTooLargeException();
            }

            if ((chunked && bytesRead > holdBox.Hold.Bytes)
                || time.GetUtcNow() - holdBox.LastTouchedAt >= HoldRenewInterval)
            {
                throw new NotSupportedException("Project uploads require asynchronous stream reading.");
            }
        }

        #endregion
    }

    #endregion
}
