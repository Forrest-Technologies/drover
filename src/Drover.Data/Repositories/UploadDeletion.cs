namespace Drover.Data.Repositories;

/// <summary>An old unsent conversation upload found across spaces, identified without exposing its contents.</summary>
public sealed record ExpiredUpload(Guid SpaceId, Guid AudienceUserId, Guid FileId, Guid ThreadId);

/// <summary>Stored bytes ready for a bounded deletion attempt, identified without exposing a storage path.</summary>
public sealed record PendingUploadToDelete(Guid SpaceId, Guid AudienceUserId, Guid FileId);
