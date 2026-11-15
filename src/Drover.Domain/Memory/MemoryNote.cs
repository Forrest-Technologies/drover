namespace Drover.Domain.Memory;

/// <summary>
/// One short note a bot keeps about the person it works for, in its own words: one fact or preference, rewritten
/// when it changes. A note belongs to one bot and, through its audience, to one person. Who wrote it is either the
/// bot or a person, never both; a note a bot wrote names the conversation and the message it came from.
/// </summary>
public sealed class MemoryNote : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The person the note is about and the only one who may read it. Everyone is reserved for notes a shared space keeps.</summary>
    public Guid AudienceUserId { get; set; }

    public Guid BotId { get; set; }

    public required string Text { get; set; }

    public Guid? WrittenByBotId { get; set; }

    public Guid? WrittenByUserId { get; set; }

    public Guid? SourceThreadId { get; set; }

    public Guid? SourceMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    #endregion
}
