namespace Drover.Domain.Spaces;

/// <summary>Limits an owner can set on a space. A null limit means the deployment policy decides.</summary>
public sealed record SpaceSettings
{
    public int? MaxBotDepth { get; init; }

    public int? MaxChildrenPerBot { get; init; }

    public int? MaxBots { get; init; }
}
