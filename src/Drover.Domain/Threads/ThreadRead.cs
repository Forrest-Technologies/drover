namespace Drover.Domain.Threads;

/// <summary>
/// How far one person has seen one thread: the last message position they had in front of them, and when. Kept on
/// the server so every device of theirs agrees. The audience is the person whose marker it is, so the database shows
/// it to them alone and nobody can write one for someone else. Like <c>runs.thread_id</c>, the thread is a plain id
/// with no foreign key: a key to the thread would have to carry the thread's audience, and a shared thread's audience
/// is everyone.
/// </summary>
public sealed class ThreadRead : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid SpaceId { get; set; }

    /// <summary>The person whose marker this is.</summary>
    public Guid AudienceUserId { get; set; }

    public Guid ThreadId { get; set; }

    /// <summary>The newest position the person had seen. Zero when the thread had no message yet.</summary>
    public int SeenThroughPosition { get; set; }

    public DateTimeOffset SeenAt { get; set; }

    #endregion
}
