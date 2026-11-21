namespace Drover.Domain.Files;

/// <summary>
/// A conversation upload whose row and quota are already gone, and whose stored bytes still need deletion. The row
/// is written in the same transaction as the file delete, so a storage failure can be retried without releasing
/// quota twice.
/// </summary>
public sealed class PendingUploadDeletion : ISpaceOwned, IAudienceScoped
{
    #region Properties

    /// <summary>The removed file's id.</summary>
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    /// <summary>When set and still in the future, a worker is deleting the stored bytes.</summary>
    public DateTimeOffset? CleanupClaimUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
