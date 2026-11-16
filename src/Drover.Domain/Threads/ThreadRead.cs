namespace Drover.Domain.Threads;

/// <summary>
/// How far one person has seen one thread: the last message position they had in front of them, and when. Kept on
/// the server so every device of theirs agrees. Its audience follows the thread through the foreign key, so a private
/// thread's marker is as private as the thread; <see cref="UserId"/> is whose marker it is.
/// </summary>
public sealed class ThreadRead : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ThreadId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The newest position the person had seen. Zero when the thread had no message yet.</summary>
    public int SeenThroughPosition { get; set; }

    public DateTimeOffset SeenAt { get; set; }

    #endregion
}
