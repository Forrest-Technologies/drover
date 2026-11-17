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
    /// How the bot keeps a long thread inside its model's context. Only <see cref="CompactionStrategy.Summarization"/>
    /// changes what the model is given, and only on a main thread that has a summary.
    /// </summary>
    public CompactionStrategy Compaction { get; init; } = CompactionStrategy.None;

    #endregion
}
