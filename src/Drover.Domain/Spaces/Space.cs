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

    #endregion
}
