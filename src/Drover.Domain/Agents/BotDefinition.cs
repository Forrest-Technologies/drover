namespace Drover.Domain.Agents;

/// <summary>The part of a bot the runtime builds an agent from.</summary>
public sealed record BotDefinition
{
    #region Properties

    public required Guid BotId { get; init; }

    public required string Name { get; init; }

    public string Instructions { get; init; } = string.Empty;

    public required Guid ModelConnectionId { get; init; }

    public required string Model { get; init; }

    #endregion
}
