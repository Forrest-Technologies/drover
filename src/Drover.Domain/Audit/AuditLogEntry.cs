namespace Drover.Domain.Audit;

/// <summary>A record of something a person or a bot did in the space. Insert and select only.</summary>
public sealed class AuditLogEntry : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public ActorKind ActorKind { get; set; }

    public Guid? ActorId { get; set; }

    public required string Action { get; set; }

    public string? TargetKind { get; set; }

    public Guid? TargetId { get; set; }

    public JsonElement Data { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
