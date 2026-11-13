namespace Drover.Domain.Bots;

/// <summary>
/// How a bot keeps a long thread inside its model's context. The names are Drover's, not a framework's.
/// </summary>
public enum CompactionStrategy
{
    /// <summary>Nothing is dropped; a thread that outgrows the model fails.</summary>
    None,

    /// <summary>Only the most recent messages are sent.</summary>
    SlidingWindow,

    /// <summary>Older messages are replaced by a summary the model writes.</summary>
    Summarization,

    /// <summary>Old tool results are shortened first; the conversation is kept.</summary>
    ToolResults,

    /// <summary>The oldest messages are cut when the limit is reached.</summary>
    Truncation,
}
