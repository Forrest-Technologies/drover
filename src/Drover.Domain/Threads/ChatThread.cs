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

    public Guid? ProjectId { get; set; }

    /// <summary>Never updated after insert: the application role has no privilege on this column.</summary>
    public Guid OwnerUserId { get; set; }

    public string? Title { get; set; }

    public ThreadOrigin Origin { get; set; } = ThreadOrigin.Chat;

    public DateTimeOffset LastActivityAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>The owner's one main conversation with the bot. A person has at most one per bot, and it takes no title.</summary>
    public bool IsMain { get; set; }

    /// <summary>
    /// The server's running summary of a main thread, rewritten as the conversation goes on. Null until one is
    /// written, and always null on a side chat. It is part of the thread's row, so it has the thread's audience.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The position of the last message the summary covers. Positions start at one and have no gaps, so this is also
    /// how many messages it covers. Written in the same statement as <see cref="Summary"/>.
    /// </summary>
    public int? SummaryThroughPosition { get; set; }

    public DateTimeOffset? SummaryUpdatedAt { get; set; }

    /// <summary>
    /// The model this conversation answers with in place of the bot's own: one of the models on the bot's model
    /// connection that the space may use. Null answers with the bot's model.
    /// </summary>
    public string? Model { get; set; }

    #endregion
}
