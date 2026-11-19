namespace Drover.Domain.Files;

/// <summary>
/// Metadata of a stored file. The bytes live behind the file store, under <see cref="StorageKey"/>. A file a person
/// attaches belongs to a thread and has the thread's audience, carried by the foreign key, so sharing the thread
/// shares its files. It is pending until a message is sent with it (<see cref="MessageId"/>), and kept as long as
/// that message is.
/// </summary>
public sealed class StoredFile : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public required string StorageKey { get; set; }

    /// <summary>The project that shares the file with the space.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>The conversation the file was attached in.</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>
    /// The message that carries the file, null while the file waits to be sent. A plain column: the thread's key
    /// already carries the audience, and a second cascading key through messages would give one row two cascades.
    /// </summary>
    public Guid? MessageId { get; set; }

    public required string Name { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    /// <summary>Lowercase hex.</summary>
    public required string Sha256 { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? CreatedByBotId { get; set; }

    /// <summary>A picture's size, from its header, so it is shown at its size before it loads.</summary>
    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>A text file's first lines, for the conversation to show.</summary>
    public string? Preview { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
