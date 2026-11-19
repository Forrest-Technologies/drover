namespace Drover.Data.Repositories;

/// <summary>
/// The rows of stored files, and the space's running total of their bytes. Runs inside the caller's tenant scope,
/// where row-level security shows a person only the files of conversations they can see.
/// </summary>
public sealed class FileRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<StoredFile?> FindFile(Guid id, CancellationToken cancellationToken) =>
        db.Files.AsNoTracking().SingleOrDefaultAsync(file => file.Id == id, cancellationToken);

    /// <summary>The files with these ids that the caller can see, in no particular order.</summary>
    public Task<List<StoredFile>> ListFiles(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Files.AsNoTracking().Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);

    /// <summary>As <see cref="ListFiles"/>, tracked, so a change to them is saved when the scope commits.</summary>
    public Task<List<StoredFile>> GetFilesForUpdate(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Files.Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);

    /// <summary>Whether a message of the thread the caller can see carries a file.</summary>
    public Task<bool> AnySentInThread(Guid threadId, CancellationToken cancellationToken) =>
        db.Files.AnyAsync(file => file.ThreadId == threadId && file.MessageId != null, cancellationToken);

    /// <summary>
    /// The files the person and the bot exchanged in the person's own conversations with it, newest first, at most
    /// <paramref name="limit"/>: each one the bot shared and each one the person sent. A file another member sent in
    /// a conversation the person can see is not theirs, and is left out.
    /// </summary>
    public async Task<List<ExchangedFile>> ListExchangedFiles(Guid botId, Guid userId, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.Files
            .AsNoTracking()
            .Where(file => file.MessageId != null && (file.CreatedByBotId == botId || file.CreatedByUserId == userId))
            .Join(
                db.Threads.Where(thread => thread.BotId == botId && thread.OwnerUserId == userId),
                file => file.ThreadId,
                thread => (Guid?)thread.Id,
                (file, thread) => new { File = file, thread.IsMain })
            .OrderByDescending(row => row.File.CreatedAt)
            .ThenByDescending(row => row.File.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new ExchangedFile(row.File, row.IsMain))];
    }

    /// <summary>The files shared in a project that the caller can see, newest first.</summary>
    public Task<List<StoredFile>> ListProjectFiles(Guid projectId, CancellationToken cancellationToken) =>
        db.Files
            .AsNoTracking()
            .Where(file => file.ProjectId == projectId)
            .OrderByDescending(file => file.CreatedAt)
            .ThenByDescending(file => file.Id)
            .ToListAsync(cancellationToken);

    public Task<StoredFile?> FindProjectFile(Guid projectId, Guid fileId, CancellationToken cancellationToken) =>
        db.Files.AsNoTracking().SingleOrDefaultAsync(file => file.Id == fileId && file.ProjectId == projectId, cancellationToken);

    public async Task<StoredFile?> GetProjectFileForUpdate(Guid projectId, Guid fileId, CancellationToken cancellationToken) =>
        (await db.Files
            .FromSql($"SELECT * FROM files WHERE id = {fileId} AND project_id = {projectId} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task<List<StoredFile>> GetProjectFilesForUpdate(Guid projectId, CancellationToken cancellationToken) =>
        db.Files
            .FromSql($"SELECT * FROM files WHERE project_id = {projectId} FOR UPDATE")
            .ToListAsync(cancellationToken);

    public void Add(StoredFile file) => db.Files.Add(file);

    public void Remove(StoredFile file) => db.Files.Remove(file);

    public void RemoveRange(IEnumerable<StoredFile> files) => db.Files.RemoveRange(files);

    /// <summary>
    /// Adds the bytes to the space's total when the total stays within <paramref name="limit"/>, in one statement,
    /// so two uploads at once cannot both pass. False, and nothing changed, when it would not.
    /// </summary>
    public async Task<bool> ReserveBytes(Guid spaceId, long bytes, long limit, CancellationToken cancellationToken) =>
        await db.Spaces
            .Where(space => space.Id == spaceId && space.StoredFileBytes + bytes <= limit)
            .ExecuteUpdateAsync(set => set.SetProperty(space => space.StoredFileBytes, space => space.StoredFileBytes + bytes), cancellationToken) == 1;

    /// <summary>Takes the bytes of a removed file off the space's total.</summary>
    public Task ReleaseBytes(Guid spaceId, long bytes, CancellationToken cancellationToken) =>
        db.Spaces
            .Where(space => space.Id == spaceId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(space => space.StoredFileBytes, space => space.StoredFileBytes > bytes ? space.StoredFileBytes - bytes : 0),
                cancellationToken);

    public async Task<long> GetStoredBytes(Guid spaceId, CancellationToken cancellationToken) =>
        await db.Spaces.Where(space => space.Id == spaceId).Select(space => space.StoredFileBytes).SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Counts <paramref name="bytes"/> against the space and records the hold, in the caller's transaction. Null when
    /// the space cannot take that many more bytes. Nothing is written to the file store yet.
    /// </summary>
    public async Task<FileByteReservation?> TryHold(Guid spaceId, Guid id, long bytes, long limit, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        if (bytes <= 0 || !await ReserveBytes(spaceId, bytes, limit, cancellationToken))
        {
            return null;
        }

        var hold = new FileByteReservation
        {
            Id = id,
            Bytes = bytes,
            CreatedAt = createdAt,
        };
        db.FileByteReservations.Add(hold);
        return hold;
    }

    /// <summary>Drops the hold and returns all of its bytes. False when the hold is already gone.</summary>
    public async Task<bool> ReleaseHold(Guid spaceId, Guid id, long bytes, CancellationToken cancellationToken)
    {
        var removed = await db.FileByteReservations.Where(hold => hold.Id == id).ExecuteDeleteAsync(cancellationToken);

        if (removed == 0)
        {
            return false;
        }

        await ReleaseBytes(spaceId, bytes, cancellationToken);
        return true;
    }

    /// <summary>
    /// Drops the hold and returns the bytes that the stored file did not use. False when the hold is already gone,
    /// in which case the caller must not keep the file.
    /// </summary>
    public async Task<bool> CompleteHold(Guid spaceId, Guid id, long heldBytes, long actualBytes, CancellationToken cancellationToken)
    {
        var removed = await db.FileByteReservations.Where(hold => hold.Id == id).ExecuteDeleteAsync(cancellationToken);

        if (removed == 0)
        {
            return false;
        }

        var slack = heldBytes - actualBytes;

        if (slack > 0)
        {
            await ReleaseBytes(spaceId, slack, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Removes the project files. The database trigger records a pending deletion and returns their quota. The rows
    /// are saved in this transaction so the pending rows can be claimed before commit.
    /// </summary>
    public async Task StageProjectFileDeletion(
        IReadOnlyCollection<StoredFile> projectFiles,
        DateTimeOffset now,
        DateTimeOffset claimUntil,
        CancellationToken cancellationToken)
    {
        if (projectFiles.Count == 0)
        {
            return;
        }

        RemoveRange(projectFiles);
        await db.SaveChangesAsync(cancellationToken);
        var ids = projectFiles.Select(file => file.Id).ToArray();
        var claimed = await ClaimPending(ids, projectId: null, now, claimUntil, cancellationToken);

        if (claimed != ids.Length)
        {
            throw new InvalidOperationException("A project file was removed without a pending deletion row.");
        }
    }

    public async Task<PendingFileDeletion?> ClaimPending(Guid id, Guid projectId, DateTimeOffset now, DateTimeOffset claimUntil, CancellationToken cancellationToken)
    {
        var claimed = await ClaimPending([id], projectId, now, claimUntil, cancellationToken);

        if (claimed == 0)
        {
            return null;
        }

        return await db.PendingFileDeletions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    public async Task<List<PendingFileDeletion>> ClaimPendingForProject(Guid projectId, DateTimeOffset now, DateTimeOffset claimUntil, CancellationToken cancellationToken)
    {
        var ids = await db.PendingFileDeletions
            .Where(row => row.ProjectId == projectId && (row.CleanupClaimUntil == null || row.CleanupClaimUntil <= now))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return [];
        }

        await ClaimPending(ids, projectId, now, claimUntil, cancellationToken);
        return await db.PendingFileDeletions.AsNoTracking().Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);
    }

    public Task ClearClaim(Guid id, CancellationToken cancellationToken) =>
        db.PendingFileDeletions
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CleanupClaimUntil, (DateTimeOffset?)null), cancellationToken);

    public Task ForgetPending(Guid id, CancellationToken cancellationToken) =>
        db.PendingFileDeletions.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken);

    #endregion

    #region Private Methods

    private Task<int> ClaimPending(IReadOnlyCollection<Guid> ids, Guid? projectId, DateTimeOffset now, DateTimeOffset claimUntil, CancellationToken cancellationToken)
    {
        var pending = db.PendingFileDeletions.Where(row => ids.Contains(row.Id) && (row.CleanupClaimUntil == null || row.CleanupClaimUntil <= now));

        if (projectId is { } project)
        {
            pending = pending.Where(row => row.ProjectId == project);
        }

        return pending.ExecuteUpdateAsync(set => set.SetProperty(row => row.CleanupClaimUntil, claimUntil), cancellationToken);
    }

    #endregion
}


/// <summary>A file a person and a bot exchanged, and whether the conversation that carries it is the main one.</summary>
public sealed record ExchangedFile(StoredFile File, bool InMainConversation);
