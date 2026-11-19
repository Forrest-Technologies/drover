namespace Drover.Domain.Projects;

/// <summary>Shared instructions and conversations grouped inside one space.</summary>
public sealed class Project : ISpaceOwned
{
    #region Properties

    public const int MaxNameLength = 200;

    public const int MaxInstructionsLength = 32_000;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required string Name { get; set; }

    public required string Instructions { get; set; }

    #endregion
}
