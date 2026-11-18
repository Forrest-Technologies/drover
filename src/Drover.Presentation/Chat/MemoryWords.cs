namespace Drover.Ui.Chat;

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

    /// <summary>The toast after a removal, which offers Undo.</summary>
    public static string Removed(string text) => $"Removed the note: {text}";

    /// <summary>What a card says when its note was changed after the bot saved it, so Forget left it alone.</summary>
    public static string ChangedSince(string botName) => $"This note has changed since {botName} saved it, so it was not forgotten.";

    /// <summary>The accessible name of the toast's Undo.</summary>
    public static string UndoRemoving(string text) => $"Undo removing the note: {text}";

    /// <summary>Why the panel is empty, and when it will not be.</summary>
    public static string Empty(string botName) =>
        $"{botName} has not noted anything about you yet. It keeps a note when it learns a preference or a fact worth keeping.";

    public static string Off(string botName) => $"Memory is off for {botName}, so it keeps no notes.";

    /// <summary>Memory off with notes kept from before: they stay until the person removes them.</summary>
    public static string OffKeeping(string botName) =>
        $"Memory is off for {botName}. It keeps these notes until you remove them, and adds no new ones.";

    /// <summary>Said only when the notes are nearly full, because that is when a person can act on it.</summary>
    public static string NearlyFull(string botName) =>
        $"{botName}'s notes are nearly full. From here it merges or replaces notes when it learns something new.";

    #endregion

    #region Fixed Words

    /// <summary>The conversation card for a changed note. Before and after are in the card's disclosure.</summary>
    public const string Updated = "Updated a note";

    /// <summary>What the card says once the person has had the bot forget the note it shows.</summary>
    public const string Forgotten = "Forgotten. The note is no longer kept.";


    /// <summary>Said when a note acted on is not there any more.</summary>
    public const string Gone = "This note is no longer kept, so there is nothing to change.";

    #endregion
}
