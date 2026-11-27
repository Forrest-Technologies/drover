namespace Drover.Domain.Memory;

/// <summary>Whether a pending forget removes one conversation or everything kept for a person in a space.</summary>
public enum MemoryForgetKind
{
    Conversation,
    Person,
}


/// <summary>
/// A durable request to tell the memory provider to forget a deleted conversation or a person removed from a space.
/// Written in the same transaction as the deletion so a failed provider call is retried on the hourly pass until the
/// provider accepts.
/// </summary>
public sealed class MemoryForgetRequest : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public MemoryForgetKind Kind { get; set; }

    public Guid PersonId { get; set; }

    public Guid? BotId { get; set; }

    public Guid? ThreadId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AttemptedAt { get; set; }

    #endregion
}
