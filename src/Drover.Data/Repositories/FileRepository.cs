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

    public void Add(StoredFile file) => db.Files.Add(file);

    public void Remove(StoredFile file) => db.Files.Remove(file);

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

    #endregion
}
