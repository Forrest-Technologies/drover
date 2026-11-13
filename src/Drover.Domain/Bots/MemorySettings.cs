namespace Drover.Domain.Bots;

/// <summary>
/// What a bot remembers and how it keeps long threads usable. Stored as JSON on the bot.
/// </summary>
public sealed record MemorySettings
{
    #region Properties

    public bool Enabled { get; init; }

    public CompactionStrategy Compaction { get; init; } = CompactionStrategy.None;

    /// <summary>The context size, in tokens, at which compaction starts. Null leaves it to the model's own limit.</summary>
    public int? CompactAtTokens { get; init; }

    /// <summary>How many of the newest messages compaction always leaves untouched.</summary>
    public int? KeepRecentMessages { get; init; }

    #endregion
}
