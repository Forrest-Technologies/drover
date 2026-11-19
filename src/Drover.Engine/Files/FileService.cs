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

        var clean = CleanName(name);
        var id = Guid.CreateVersion7();
        var key = FileStorageKeys.For(SpaceId, id);
        var inspector = new FileInspector();
        var limit = Math.Min(Rules.MaxFileBytes, prepared.Room);
        await using var metered = new MeteredStream(content, limit, inspector);

        try
        {
            await store.Save(SpaceId, key, metered, cancellationToken);
        }
        catch (FileTooLargeException tooLarge)
        {
            await store.Delete(SpaceId, key, CancellationToken.None);
            logger.LogInformation(tooLarge, "An upload to project {ProjectId} went past {MaxBytes} bytes and was refused", projectId, limit);
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
                file.ReadingNote = await ReadingNote(file, modelReadsImages: false, cancellationToken);
            }

            upload = await KeepForProject(projectId, file, cancellationToken);
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
        StoredFile? file;

        await using (var work = await scope.Begin(cancellationToken))
        {
            file = await files.GetProjectFileForUpdate(projectId, fileId, cancellationToken);

            if (file is null)
            {
                return false;
            }

            files.Remove(file);
            await files.ReleaseBytes(SpaceId, file.Size, cancellationToken);
            await work.Commit(cancellationToken);
        }

        await store.Delete(SpaceId, file.StorageKey, CancellationToken.None);
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

        var stored = await files.GetStoredBytes(SpaceId, cancellationToken);
        await work.Commit(cancellationToken);

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
            return Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes));
        }

        file.ThreadId = thread.Id;
        file.AudienceUserId = thread.AudienceUserId;
        files.Add(file);
        await work.Commit(cancellationToken);
        logger.LogInformation("File {FileId} of {Size} bytes stored for thread {ThreadId}", file.Id, file.Size, threadId);
        return new FileUpload(file);
    }

    private async Task<FileUpload> KeepForProject(Guid projectId, StoredFile file, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await projects.LockProjectForShare(projectId, cancellationToken) is null)
        {
            return new FileUpload(null, FileProblem.NotFound);
        }

        if (!await files.ReserveBytes(SpaceId, file.Size, MaxSpaceBytes, cancellationToken))
        {
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

    #endregion
}
