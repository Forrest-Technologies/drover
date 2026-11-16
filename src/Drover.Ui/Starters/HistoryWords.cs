namespace Drover.Ui.Starters;

/// <summary>Who made a change to a bot.</summary>
public enum HistoryActor
{
    You,
    Member,
    Bot,
}

/// <summary>One value a change touched, before and after. <see cref="Field"/> is the stored name, such as "instructions".</summary>
public sealed record HistoryChange(string Field, string? Before, string? After);

/// <summary>
/// One entry in a bot's history: everything one save changed. <see cref="ForName"/> is the person a bot acted
/// for when a bot made the change. <see cref="CanRestore"/> is true when the entry changed the bot's definition,
/// which is the only thing a restore puts back.
/// </summary>
public sealed record HistoryEntry(
    Guid Id,
    HistoryActor Actor,
    string ActorName,
    string? ForName,
    string Action,
    DateTimeOffset When,
    IReadOnlyList<HistoryChange> Changes,
    bool CanRestore);

/// <summary>A bot's history in sentences, built from the audit log's action names and changed values.</summary>
public static class HistoryWords
{
    #region Private Fields

    private static readonly Dictionary<string, string> fields = new(StringComparer.Ordinal)
    {
        ["name"] = "the name",
        ["handle"] = "the handle",
        ["description_for_bots"] = "what the bot is for",
        ["instructions"] = "the instructions",
        ["model"] = "the model",
        ["model_settings"] = "the model settings",
        ["memory"] = "the memory settings",
        ["sandbox"] = "the sandbox settings",
        ["build_limits"] = "the limits on building bots",
    };

    private static readonly Dictionary<string, string> actions = new(StringComparer.Ordinal)
    {
        ["bot.created"] = "made this bot",
        ["bot.duplicated"] = "made this bot as a copy",
        ["bot.paused"] = "paused the bot",
        ["bot.resumed"] = "resumed the bot",
        ["bot.removed"] = "removed the bot",
        ["bot.restored"] = "restored an earlier version",
        ["grant.changed"] = "changed what a tool may do",
        ["schedule.created"] = "added a routine",
        ["schedule.updated"] = "changed a routine",
        ["schedule.removed"] = "removed a routine",
        ["rule.created"] = "added a rule",
        ["rule.updated"] = "changed a rule",
        ["rule.removed"] = "removed a rule",
        ["sandbox.paused"] = "paused the sandbox",
        ["sandbox.resumed"] = "resumed the sandbox",
        ["sandbox.rebuilt"] = "rebuilt the sandbox",
        ["sandbox.turned_off"] = "turned the sandbox off",
        ["sandbox.command"] = "ran a command in the sandbox",
        ["sandbox.input"] = "used the sandbox screen",
    };

    #endregion

    #region Public Methods

    /// <summary>Who did it: "You", a member's name, or "Sample lead, for Sample person".</summary>
    public static string Who(HistoryEntry entry) => entry switch
    {
        { Actor: HistoryActor.You } => "You",
        { Actor: HistoryActor.Bot, ForName: { Length: > 0 } person } => $"{entry.ActorName}, for {person},",
        _ => entry.ActorName,
    };

    /// <summary>
    /// What was done. A definition change names what changed: "changed the instructions and the name". An action
    /// this build does not know is said as "made a change" with its stored name, never guessed at.
    /// </summary>
    public static string What(HistoryEntry entry)
    {
        if (entry.Action is "bot.updated")
        {
            var changed = entry.Changes.Select(change => Field(change.Field)).Distinct().ToArray();
            return changed.Length is 0 ? "saved the bot without changes" : $"changed {Words.List(changed)}";
        }

        return actions.GetValueOrDefault(entry.Action) ?? $"made a change ({entry.Action})";
    }

    /// <summary>The entry as one sentence: "You changed the instructions".</summary>
    public static string Sentence(HistoryEntry entry) => $"{Who(entry)} {What(entry)}";

    /// <summary>A stored field name as a person says it. An unknown one is shown as it is stored.</summary>
    public static string Field(string field) => fields.GetValueOrDefault(field) ?? field;

    /// <summary>A field's name as a heading: "The instructions".</summary>
    public static string Heading(string field)
    {
        var words = Field(field);
        return words.Length is 0 ? words : $"{char.ToUpperInvariant(words[0])}{words[1..]}";
    }

    /// <summary>True for a value long enough to compare line by line instead of side by side.</summary>
    public static bool IsLong(HistoryChange change) =>
        (change.Before?.Contains('\n') ?? false) || (change.After?.Contains('\n') ?? false)
        || Math.Max(change.Before?.Length ?? 0, change.After?.Length ?? 0) > 80;

    #endregion
}
