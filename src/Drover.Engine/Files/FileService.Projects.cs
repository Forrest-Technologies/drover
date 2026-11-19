namespace Drover.Engine.Files;

public sealed partial class FileService
{
    #region Public Methods

    public async Task<FileUpload> UploadProject(
        Guid projectId,
        string? name,
        Stream content,
        long? declaredLength,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var room = await ReadProjectRoom(projectId, cancellationToken);
        return room switch
        {
            null => new FileUpload(null, FileProblem.NotFound),
            _ when declaredLength > Rules.MaxFileBytes =>
                Refused(FileRefusals.TooLarge, FileWords.TooLarge(declaredLength, Rules.MaxFileBytes)),
            _ when declaredLength > room =>
                Refused(FileRefusals.SpaceFull, FileWords.SpaceFull(Rules.MaxSpaceFileMegabytes)),
            { } available => await StoreUpload(
                projectId, true, name, content, userId, new(string.Empty, null, available, null), cancellationToken),
        };
    }

    public async Task<List<StoredFile>?> ListProject(Guid projectId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        return await projects.Exists(projectId, cancellationToken)
            ? await files.ListProjectFiles(projectId, cancellationToken)
            : null;
    }

    public async Task<FileDownload?> OpenProject(Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        StoredFile? file;
        await using (var work = await scope.Begin(cancellationToken))
        {
            file = await files.FindProjectFile(projectId, fileId, cancellationToken);
        }

        return await OpenStoredFile(file, cancellationToken);
    }

    public async Task<bool> RemoveProjectFile(Guid projectId, Guid fileId, CancellationToken cancellationToken)
    {
        StoredFile? file;
        await using (var work = await scope.Begin(cancellationToken))
        {
            file = await files.FindProjectFile(projectId, fileId, cancellationToken);
        }

        if (file is null)
        {
            return false;
        }

        // Keep the row and its quota until deleting bytes succeeds. A retry can delete the same key again.
        await store.Delete(SpaceId, file.StorageKey, cancellationToken);

        await using var removal = await scope.Begin(cancellationToken);
        var removed = await projects.GetForUpdate(projectId, cancellationToken) is not null
            && await files.RemoveProjectFiles(projectId, [file.Id], cancellationToken) == 1;
        if (removed)
        {
            await files.ReleaseBytes(SpaceId, file.Size, cancellationToken);
        }

        await removal.Commit(cancellationToken);
        return removed;
    }

    public async Task<bool> RemoveProject(
        Guid projectId, Guid actorUserId, ProjectService projectService, CancellationToken cancellationToken)
    {
        var cleared = new HashSet<Guid>();
        while (await ListProject(projectId, cancellationToken) is { } snapshot)
        {
            foreach (var file in snapshot.Where(file => !cleared.Contains(file.Id)))
            {
                await store.Delete(SpaceId, file.StorageKey, cancellationToken);
                cleared.Add(file.Id);
            }

            await using var work = await scope.Begin(cancellationToken);
            if (await projects.GetForUpdate(projectId, cancellationToken) is null)
            {
                return false;
            }

            // Uploads take the same project lock before keeping their rows. Retry if one finished during cleanup.
            var current = await files.ListProjectFiles(projectId, cancellationToken);
            if (current.Any(file => !cleared.Contains(file.Id)))
            {
                continue;
            }

            await files.RemoveProjectFiles(projectId, cleared, cancellationToken);
            await files.ReleaseBytes(SpaceId, current.Sum(file => file.Size), cancellationToken);
            await projectService.Remove(projectId, actorUserId, cancellationToken);
            await work.Commit(cancellationToken);
            return true;
        }

        return false;
    }

    #endregion

    #region Private Methods

    private async Task<long?> ReadProjectRoom(Guid projectId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        return await projects.Exists(projectId, cancellationToken)
            ? Math.Max(0, MaxSpaceBytes - await files.GetStoredBytes(SpaceId, cancellationToken))
            : null;
    }

    #endregion
}
