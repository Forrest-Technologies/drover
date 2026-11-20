namespace Drover.Domain.Projects;

/// <summary>
/// A work area inside one space that groups conversations and holds shared instructions. A project belongs to no
/// bot and has no audience of its own: every member of the space reads it, and owners and admins change it.
/// </summary>
public sealed class Project : ISpaceOwned
{
    #region Private Fields

    public const int MaxNameLength = 200;

    public const int MaxInstructionsLength = 32_000;

    #endregion

    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required string Name { get; set; }

    public string Instructions { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    #endregion
}
