namespace Drover.Ui.Starters;

/// <summary>What the memory panel has to show.</summary>
public enum MemoryPanelState
{
    Loading,
    Failed,
    Off,
    Ready,
}

/// <summary>
/// One thing a bot remembers about the person reading. <see cref="OriginHref"/> opens the moment in the
/// conversation where the bot wrote it; a note the person wrote has none.
/// </summary>
public sealed record MemoryNote(Guid Id, string Text, bool WrittenByBot, DateTimeOffset When, string? OriginHref);

/// <summary>A bot's memory in words: where a note came from, and what its tool calls read as in the conversation.</summary>
public static class MemoryWords
{
    #region Public Methods

    /// <summary>Who wrote a note and when, given the moment already in words.</summary>
    public static string Origin(MemoryNote note, string botName, string moment) =>
        note.WrittenByBot ? $"{botName} noted this {moment}" : $"You wrote this {moment}";

    /// <summary>The conversation card for a saved note: the note itself, so nothing has to be opened to read it.</summary>
    public static string Remembered(string text) => $"Remembered: {text}";

    public static string Forgot(string text) => $"Forgot: {text}";

    /// <summary>Why the panel is empty, and when it will not be.</summary>
    public static string Empty(string botName) =>
        $"{botName} has not noted anything about you yet. It keeps a note when it learns a preference or a fact worth keeping.";

    public static string Off(string botName) => $"Memory is off for {botName}, so it keeps no notes.";

    /// <summary>Said only when the notes are nearly full, because that is when a person can act on it.</summary>
    public static string NearlyFull(string botName) =>
        $"{botName}'s notes are nearly full. From here it merges or replaces notes when it learns something new.";

    #endregion
}
