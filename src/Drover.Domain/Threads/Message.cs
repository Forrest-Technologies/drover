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

    public MessageRole Role { get; set; }

    public Guid? AuthorUserId { get; set; }

    public Guid? AuthorBotId { get; set; }

    public Guid? RunId { get; set; }

    public IReadOnlyList<MessagePart> Parts { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
