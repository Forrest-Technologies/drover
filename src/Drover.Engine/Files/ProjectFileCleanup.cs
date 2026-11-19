namespace Drover.Engine.Files;

/// <summary>One project file whose row is gone and whose stored bytes still need deleting.</summary>
internal readonly record struct StoredDeletion(Guid Id, string StorageKey);

/// <summary>
/// Deletes stored bytes for project files whose rows were already removed, and leaves a pending row claimable when a
/// delete fails so a later call can finish it.
/// </summary>
internal static class ProjectFileCleanup
{
    #region Private Fields

    public static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);

    #endregion

    #region Public Methods

    public static async Task Finish(
        TenantScope scope,
        FileRepository files,
        IFileStore store,
        Guid spaceId,
        IReadOnlyList<StoredDeletion> pending,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var failed = false;

        foreach (var row in pending)
        {
            try
            {
                await store.Delete(spaceId, row.StorageKey, CancellationToken.None);

                await using var work = await scope.Begin(cancellationToken);
                await files.ForgetPending(row.Id, cancellationToken);
                await work.Commit(cancellationToken);
            }
            catch (Exception exception)
            {
                failed = true;
                logger.LogWarning(exception, "Stored bytes for file {FileId} stayed after its row was removed", row.Id);
                await ClearClaim(scope, files, row.Id, cancellationToken);
            }
        }

        if (failed)
        {
            throw new IOException("Stored bytes for a removed project file could not be deleted.");
        }
    }

    #endregion

    #region Private Methods

    private static async Task ClearClaim(TenantScope scope, FileRepository files, Guid id, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        await files.ClearClaim(id, cancellationToken);
        await work.Commit(cancellationToken);
    }

    #endregion
}
