namespace Drover.Domain.Threads;

/// <summary>One message of a thread. The audience is inherited from the thread through the foreign key.</summary>
public sealed class Message : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ThreadId { get; set; }

    /// <summary>
    /// Orders the thread. Null while the message is pending: sent while the bot was working, stored, and not yet part
    /// of the conversation the model reads. It gets its position when a run takes it.
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    /// A pending message that steers: the run working on the thread takes it at its next step, if that run acts for
    /// the message's author. Otherwise the message waits for the run to end and starts the next one.
    /// </summary>
    public bool Steers { get; set; }

    /// <summary>
    /// A rule of the bot did not let this message through. It stays in the thread for the people who can read it,
    /// and no model is ever given it.
    /// </summary>
    public bool Withheld { get; set; }

    /// <summary>
    /// The message before this one in its version of the conversation; null for the first. The messages that share a
    /// parent are versions of one place in the conversation: a person's message and its edits, or the replies to one
    /// message. Set when the message gets its position.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// When the message left the conversation that is shown: a person edited a message at or before it, asked for
    /// another reply, or switched to another version. It is kept and comes back when its version is chosen again.
    /// Null while it is part of the conversation people see and the model reads.
    /// </summary>
    public DateTimeOffset? ShelvedAt { get; set; }

    public MessageRole Role { get; set; }

    public Guid? AuthorUserId { get; set; }

    public Guid? AuthorBotId { get; set; }

    public Guid? RunId { get; set; }

    /// <summary>
    /// The schedule that sent this message for its author, who did not type it. Kept when the schedule is removed, so
    /// the message still reads as sent by a schedule.
    /// </summary>
    public Guid? ScheduleId { get; set; }

    public IReadOnlyList<MessagePart> Parts { get; set; } = [];

    /// <summary>The model that wrote a bot's reply. Null on every other message, and on a reply written before this was kept.</summary>
    public string? Model { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
