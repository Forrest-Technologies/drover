namespace Drover.Engine.Files;

/// <summary>One project file whose row is gone and whose stored bytes still need deleting.</summary>
internal readonly record struct StoredDeletion(Guid Id);

/// <summary>
/// Deletes stored bytes for project files whose rows were already removed, and leaves a pending row claimable when a
/// delete fails so a later call can finish it. Uses a bounded timeout independent of request cancellation so a dropped
/// connection does not leave the pending row claimed until its claim window expires.
/// </summary>
internal static class ProjectFileCleanup
{
    #region Private Fields

    public static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan cleanupTimeout = TimeSpan.FromSeconds(15);

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
        _ = cancellationToken;
        var failed = false;

        foreach (var row in pending)
        {
            using var timeout = new CancellationTokenSource(cleanupTimeout);

            try
            {
                await store.Delete(spaceId, FileStorageKeys.For(spaceId, row.Id), timeout.Token);

                await using var work = await scope.Begin(timeout.Token);
                await files.ForgetPending(row.Id, timeout.Token);
                await work.Commit(timeout.Token);
            }
            catch (Exception exception)
            {
                failed = true;
                logger.LogWarning(exception, "Stored bytes for file {FileId} stayed after its row was removed", row.Id);
                await ClearClaim(scope, files, row.Id, logger, timeout.Token);
            }
        }

        if (failed)
        {
            throw new IOException("Stored bytes for a removed project file could not be deleted.");
        }
    }

    #endregion

    #region Private Methods

    private static async Task ClearClaim(
        TenantScope scope,
        FileRepository files,
        Guid id,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var work = await scope.Begin(cancellationToken);
            await files.ClearClaim(id, cancellationToken);
            await work.Commit(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clear deletion claim for pending file {FileId}", id);
        }
    }

    #endregion
}
