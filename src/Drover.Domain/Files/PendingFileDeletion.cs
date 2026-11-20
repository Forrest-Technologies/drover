namespace Drover.Domain.Files;

/// <summary>
/// A project file whose row and quota are already gone, and whose stored bytes still need to be deleted. The row is
/// written in the same transaction as the file delete, so a storage failure or a retry can finish the bytes.
/// </summary>
public sealed class PendingFileDeletion : ISpaceOwned
{
    #region Properties

    /// <summary>The removed file's id.</summary>
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }

    /// <summary>The project the file belonged to. Plain column: the project may already be gone.</summary>
    public Guid ProjectId { get; set; }

    public required string StorageKey { get; set; }

    public long Size { get; set; }

    /// <summary>When set and still in the future, a caller is already deleting the stored bytes.</summary>
    public DateTimeOffset? CleanupClaimUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
