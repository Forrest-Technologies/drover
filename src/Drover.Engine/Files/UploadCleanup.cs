namespace Drover.Engine.Files;

/// <summary>
/// Removes conversation uploads that stayed unsent for one day. Cross-space discovery returns ids only. Each file
/// is re-read and removed inside its own tenant transaction, while stored bytes are deleted between transactions.
/// </summary>
public sealed class UploadCleanup(RunScopes scopes, ILogger<UploadCleanup> logger)
{
    #region Private Fields

    private const int MaxPerPass = 50;

    private static readonly TimeSpan claimWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan deleteTimeout = TimeSpan.FromSeconds(15);

    #endregion

    #region Properties

    /// <summary>How long an unsent conversation upload is kept.</summary>
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(1);

    /// <summary>How often the server looks for abandoned uploads.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMinutes(1);

    #endregion

    #region Public Methods

    /// <summary>Runs one bounded pass and returns how many file rows it removed.</summary>
    public async Task<int> Sweep(CancellationToken cancellationToken)
    {
        var expired = await scopes.WithoutTenant(
            services => services.GetRequiredService<FileRepository>()
                .ListExpiredUploads(MaxPerPass, cancellationToken),
            cancellationToken);
        var removed = 0;

        foreach (var upload in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                removed += await Stage(upload, cancellationToken) ? 1 : 0;
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    failure,
                    "Removing abandoned upload {FileId} failed before its stored bytes were handled",
                    upload.FileId);
            }
        }

        var pending = await scopes.WithoutTenant(
            services => services.GetRequiredService<FileRepository>()
                .ClaimPendingUploadDeletions(MaxPerPass, cancellationToken),
            cancellationToken);

        foreach (var deletion in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await DeleteStored(deletion, cancellationToken);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    failure,
                    "Stored bytes for abandoned upload {FileId} remain for a later retry",
                    deletion.FileId);
            }
        }

        return removed;
    }

    /// <summary>Claims and finishes a deletion just staged by an explicit remove request.</summary>
    public async Task Finish(Guid spaceId, Guid audienceUserId, Guid fileId, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(spaceId, audienceUserId);
        var claimed = await tenant.Transact(
            services => services.GetRequiredService<FileRepository>()
                .ClaimPendingUploadDeletion(fileId, claimWindow, cancellationToken),
            cancellationToken);

        if (claimed)
        {
            await DeleteStored(new PendingUploadToDelete(spaceId, audienceUserId, fileId), cancellationToken);
        }
    }

    #endregion

    #region Private Methods

    private async Task<bool> Stage(ExpiredUpload upload, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(upload.SpaceId, upload.AudienceUserId);
        return await tenant.Transact(
            async services =>
            {
                var threads = services.GetRequiredService<ThreadRepository>();

                if (await threads.GetThreadForUploadCleanup(upload.ThreadId, cancellationToken) is null)
                {
                    return false;
                }

                var files = services.GetRequiredService<FileRepository>();
                var file = await files.GetExpiredUploadForUpdate(upload.FileId, upload.ThreadId, cancellationToken);

                if (file is null || file.AudienceUserId != upload.AudienceUserId)
                {
                    return false;
                }

                await files.StageUploadDeletion(file, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task DeleteStored(PendingUploadToDelete deletion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deleteTimeout);
        var tenant = scopes.For(deletion.SpaceId, deletion.AudienceUserId);

        await tenant.InScope(services => services.GetRequiredService<IFileStore>().Delete(
            deletion.SpaceId,
            FileStorageKeys.For(deletion.SpaceId, deletion.FileId),
            timeout.Token));
        await tenant.Transact(
            services => services.GetRequiredService<FileRepository>()
                .ForgetPendingUploadDeletion(deletion.FileId, timeout.Token),
            timeout.Token);
    }

    #endregion
}
