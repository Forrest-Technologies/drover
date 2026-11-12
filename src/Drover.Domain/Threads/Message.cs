namespace Drover.Domain.Threads;

/// <summary>One message of a thread. The audience is inherited from the thread through the foreign key.</summary>
public sealed class Message : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ThreadId { get; set; }

    /// <summary>Orders the thread. Assigned under a lock on the thread row.</summary>
    public int Position { get; set; }

    public MessageRole Role { get; set; }

    public Guid? AuthorUserId { get; set; }

    public Guid? AuthorBotId { get; set; }

    public Guid? RunId { get; set; }

    public IReadOnlyList<MessagePart> Parts { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
