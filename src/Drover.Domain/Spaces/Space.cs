namespace Drover.Domain.Spaces;

/// <summary>A tenant. A personal space has one member; a shared space is a group.</summary>
public sealed class Space
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required SpaceKind Kind { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public SpaceSettings Settings { get; set; } = new();

    /// <summary>
    /// The bytes of every file the space keeps, counted across all its members, private files included, so the
    /// space's limit can be held without anyone reading another member's rows. Changed only by the file repository,
    /// in the statement that checks the limit.
    /// </summary>
    public long StoredFileBytes { get; set; }

    #endregion
}
