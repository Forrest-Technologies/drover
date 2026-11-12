namespace Drover.Domain.Threads;

/// <summary>
/// A conversation with one bot. Private to its owner by default (the audience is the owner); sharing it sets the
/// audience to everyone, and the database carries that to its messages, events, tool calls, and sessions. Named
/// <c>ChatThread</c> so it never collides with <see cref="System.Threading.Thread"/>.
/// </summary>
public sealed class ChatThread : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The owner (private) or <see cref="Audience.Everyone"/> (shared). Nothing else is allowed.</summary>
    public Guid AudienceUserId { get; set; }

    public Guid BotId { get; set; }

    /// <summary>Never updated after insert: the application role has no privilege on this column.</summary>
    public Guid OwnerUserId { get; set; }

    public string? Title { get; set; }

    public ThreadOrigin Origin { get; set; } = ThreadOrigin.Chat;

    public DateTimeOffset LastActivityAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    #endregion
}
