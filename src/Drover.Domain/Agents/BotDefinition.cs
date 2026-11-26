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

    /// <summary>
    /// How the history provider shortens the conversation for the next model call.
    /// </summary>
    public CompactionStrategy Compaction { get; init; } = CompactionStrategy.None;

    public int? CompactAtTokens { get; init; }

    public int? KeepRecentMessages { get; init; }

    #endregion
}
