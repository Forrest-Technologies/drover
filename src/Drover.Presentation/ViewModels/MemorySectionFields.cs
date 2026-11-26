namespace Drover.Presentation.ViewModels;

/// <summary>The Memory section of the bot editor: its words and its choices. The fields write straight to the draft.</summary>
public static class MemorySectionFields
{
    #region Private Fields

    private static readonly string[] strategies = ["none", "sliding_window", "summarization", "tool_results", "truncation"];

    #endregion

    #region Properties

    public static string Note =>
        "With Remember across conversations on, the bot keeps notes on what it learns about each person it talks to. Summarizes older messages works in a bot's main conversation. Before a model call, Drover can leave out older messages or shorten a tool result to fit the request. Your conversation keeps the original messages.";

    public static string RememberLabel => "Remember across conversations";

    public static string CompactionLabel => "When a conversation grows long";

    public static IReadOnlyList<ChoiceOption> Strategies => [.. strategies.Select(strategy => new ChoiceOption(strategy, BotSummaries.Compaction(strategy)))];

    public static string CompactAtLabel => "Start at this many tokens";

    public static string CompactAtPlaceholder => "The model's own limit";

    public static string KeepRecentLabel => "Always keep the newest messages";

    public static string KeepRecentPlaceholder => "None kept apart";

    #endregion

    #region Public Methods

    /// <summary>The two numbers mean something only for a way of shortening a conversation.</summary>
    public static bool NumbersShown(BotDraft draft) => draft.Compaction is not "none";

    #endregion
}
