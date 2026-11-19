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

    /// <summary>Whether the project has a shared file the caller can see.</summary>
    public Task<bool> AnyProjectFile(Guid projectId, CancellationToken cancellationToken) =>
        db.Files.AnyAsync(file => file.ProjectId == projectId, cancellationToken);

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
    /// Drops the hold and returns the bytes that the stored file did not use. Reports whether the hold completed,
    /// did not cover <paramref name="actualBytes"/>, or had already expired.
    /// </summary>
    public async Task<HoldCompletion> CompleteHold(
        Guid spaceId,
        Guid id,
        long actualBytes,
        CancellationToken cancellationToken)
    {
        var hold = await db.FileByteReservations
            .AsNoTracking()
            .Where(row => row.Id == id && row.SpaceId == spaceId)
            .Select(row => new { row.Id, row.Bytes })
            .SingleOrDefaultAsync(cancellationToken);

        if (hold is null)
        {
            return HoldCompletion.Expired;
        }

        var removed = await db.FileByteReservations
            .Where(row => row.Id == id && row.SpaceId == spaceId)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed == 0)
        {
            return HoldCompletion.Expired;
        }

        if (actualBytes > hold.Bytes)
        {
            await ReleaseBytes(spaceId, hold.Bytes, cancellationToken);
            return HoldCompletion.SpaceFull;
        }

        var slack = hold.Bytes - actualBytes;

        if (slack > 0)
        {
            await ReleaseBytes(spaceId, slack, cancellationToken);
        }

        return HoldCompletion.Completed;
    }

    /// <summary>Renews an active reservation's activity timestamp. False when the reservation already expired.</summary>
    public async Task<bool> RenewHold(
        Guid spaceId,
        Guid holdId,
        DateTimeOffset touchedAt,
        CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_byte_reservations SET created_at = {touchedAt}
            WHERE id = {holdId} AND space_id = {spaceId}
            """,
            cancellationToken) == 1;

    /// <summary>
    /// Expands an existing reservation by <paramref name="additionalBytes"/> and renews its activity timestamp to
    /// <paramref name="touchedAt"/> when the space has room under <paramref name="limit"/>.
    /// </summary>
    public async Task<HoldExtension> ExtendHold(
        Guid spaceId,
        Guid holdId,
        long additionalBytes,
        long limit,
        DateTimeOffset touchedAt,
        CancellationToken cancellationToken)
    {
        if (!await db.FileByteReservations.Where(hold => hold.Id == holdId).AnyAsync(cancellationToken))
        {
            return HoldExtension.Expired;
        }

        if (additionalBytes <= 0 || !await ReserveBytes(spaceId, additionalBytes, limit, cancellationToken))
        {
            return HoldExtension.SpaceFull;
        }

        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE file_byte_reservations SET bytes = bytes + {additionalBytes}, created_at = {touchedAt}
            WHERE id = {holdId} AND space_id = {spaceId}
            """,
            cancellationToken);

        if (updated == 0)
        {
            await ReleaseBytes(spaceId, additionalBytes, cancellationToken);
            return HoldExtension.Expired;
        }

        return HoldExtension.Extended;
    }

    /// <summary>
    /// Drops project-upload holds last renewed at or before <paramref name="olderThan"/> and returns held bytes.
    /// Runs only on administrator project-upload paths, skips locked holds, and protects visible stored
    /// or queued files.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ReleaseExpiredHolds(
        Guid spaceId,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
    {
        var expired = await db.FileByteReservations
            .FromSql($"""
                SELECT * FROM file_byte_reservations
                WHERE space_id = {spaceId} AND created_at <= {olderThan}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return [];
        }

        var expiredIds = expired.Select(hold => hold.Id).ToArray();
        var committedIds = await db.Files
            .AsNoTracking()
            .Where(file => expiredIds.Contains(file.Id))
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
        var pendingIds = await db.PendingFileDeletions
            .AsNoTracking()
            .Where(row => expiredIds.Contains(row.Id))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var protectedIds = committedIds.Concat(pendingIds).ToHashSet();
        var unattached = expired.Where(hold => !protectedIds.Contains(hold.Id)).ToList();

        db.FileByteReservations.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);

        var release = unattached.Sum(hold => hold.Bytes);

        if (release > 0)
        {
            await ReleaseBytes(spaceId, release, cancellationToken);
        }

        return [.. unattached.Select(hold => hold.Id)];
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

/// <summary>Whether a finishing project upload's reservation was consumed, exceeded, or already expired.</summary>
public enum HoldCompletion
{
    Completed,
    SpaceFull,
    Expired,
}

/// <summary>Whether an active upload reservation could be expanded and renewed.</summary>
public enum HoldExtension
{
    Extended,
    SpaceFull,
    Expired,
}
