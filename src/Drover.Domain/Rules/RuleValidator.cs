namespace Drover.Domain.Rules;

/// <summary>
/// Checks a rule when it is saved. A rule that cannot be enforced exactly as written is refused here, in words,
/// so that no rule is ever skipped, guessed at, or half applied during a run.
/// </summary>
public static class RuleValidator
{
    #region Private Fields

    public const int MaxRulesPerBot = 50;

    public const int MaxConditions = 10;

    public const int MaxNameLength = 100;

    public const int MaxListLength = 50;

    public const int MaxValueLength = 200;

    public const long MaxLimit = 1_000_000_000;

    #endregion

    #region Public Methods

    /// <summary>What is wrong with the rule, as a sentence a person can act on, or null when it holds.</summary>
    public static string? FindProblem(string? name, RuleCheckpoint checkpoint, RuleEffect effect, IReadOnlyList<RuleCondition>? conditions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return $"Give the rule a name of up to {MaxNameLength} characters.";
        }

        if (conditions is null || conditions.Count > MaxConditions)
        {
            return $"A rule has at most {MaxConditions} conditions.";
        }

        return conditions.Select(condition => FindProblem(condition, checkpoint)).FirstOrDefault(problem => problem is not null)
            ?? FindEffectProblem(checkpoint, effect, conditions);
    }

    #endregion

    #region Private Methods

    private static string? FindEffectProblem(RuleCheckpoint checkpoint, RuleEffect effect, IReadOnlyList<RuleCondition> conditions)
    {
        if (effect is RuleEffect.Ask && checkpoint is not RuleCheckpoint.BeforeTool)
        {
            return "A person can be asked first only before a tool is used. For this moment, choose block or stop.";
        }

        if (effect is RuleEffect.Redact && checkpoint is RuleCheckpoint.MessageArrives)
        {
            return "A person's own message cannot be redacted: they have already written it. Choose block or stop.";
        }

        if (conditions.Any(condition => condition is { Kind: RuleConditionKind.NotInList, Argument: not null })
            && !conditions.Any(condition => condition.Kind is RuleConditionKind.ToolIs))
        {
            return "A list for a named input has to name the tools it is about: add a condition that says which tools. To check recipients whatever the tool, leave the input unnamed, and every email address in the tool's input is checked.";
        }

        return effect is RuleEffect.Redact && !conditions.Any(FindsText)
            ? "To redact, a rule needs a condition that finds the text to remove: contains, matches a pattern, or holds a built-in kind."
            : null;
    }

    private static bool FindsText(RuleCondition condition) =>
        condition.Kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds;

    private static string? FindProblem(RuleCondition condition, RuleCheckpoint checkpoint)
    {
        var atATool = checkpoint is RuleCheckpoint.BeforeTool or RuleCheckpoint.AfterTool;

        if (condition.Argument is not null && checkpoint is not RuleCheckpoint.BeforeTool)
        {
            return "A condition can name an argument only before a tool is used. Elsewhere it looks at the whole text.";
        }

        if (condition.Argument is { } named && RuleReading.Names(named).Length == 0)
        {
            return "Name the input the condition looks at, or several parted by commas.";
        }

        if (condition.Argument is { } argument && (argument.Length == 0 || argument.Length > MaxValueLength))
        {
            return $"Name the argument in up to {MaxValueLength} characters.";
        }

        return condition.Kind switch
        {
            RuleConditionKind.ToolIs when !atATool => "A condition about the tool belongs to a rule about a tool: before a tool is used, or after it answers.",
            RuleConditionKind.ToolKind when !atATool => "A condition about the tool belongs to a rule about a tool: before a tool is used, or after it answers.",
            RuleConditionKind.ToolIs => FindListProblem(condition.Tools, "Name at least one tool", "tools"),
            RuleConditionKind.ToolKind => condition.ToolKinds.Count == 0 || condition.ToolKinds.Any(kind => !Enum.IsDefined(kind))
                ? "Choose whether the tool reads, writes, or is critical."
                : null,
            RuleConditionKind.TextContains => FindListProblem(condition.Entries, "Write at least one piece of text to look for", "pieces of text"),
            RuleConditionKind.TextMatches => RulePatterns.Compile(condition.Pattern).Problem,
            RuleConditionKind.TextHolds => condition.Builtin is { } builtin && Enum.IsDefined(builtin)
                ? null
                : "Choose what the text holds: an email address, a card number, or a key.",
            RuleConditionKind.NotInList => FindListProblem(condition.Entries, "Write at least one value for the list", "values"),
            RuleConditionKind.TimeIs => FindTimeProblem(condition),
            RuleConditionKind.RunExceeds => condition.Counter is { } counter && Enum.IsDefined(counter) && condition.Limit is >= 0 and <= MaxLimit
                ? null
                : $"Choose tool calls or tokens, and a number from 0 to {MaxLimit}.",
            _ => "That is not a kind of condition a rule can have.",
        };
    }

    private static string? FindListProblem(IReadOnlyList<string> list, string empty, string plural)
    {
        if (list.Count == 0 || list.Any(string.IsNullOrWhiteSpace))
        {
            return $"{empty}, with no blank entry.";
        }

        return list.Count > MaxListLength || list.Any(entry => entry.Length > MaxValueLength)
            ? $"Keep it to {MaxListLength} {plural} of up to {MaxValueLength} characters each."
            : null;
    }

    private static string? FindTimeProblem(RuleCondition condition)
    {
        if (!TimeOnly.TryParseExact(condition.From, "HH:mm", out var from) || !TimeOnly.TryParseExact(condition.To, "HH:mm", out var to))
        {
            return "Write the hours as two times of day, such as 22:00 and 07:00.";
        }

        if (from == to)
        {
            return "The two times are the same, so the hours cover nothing. Leave the condition out to mean always.";
        }

        if (condition.Weekdays.Any(day => !Enum.IsDefined(day)) || condition.Weekdays.Distinct().Count() != condition.Weekdays.Count)
        {
            return "Choose each weekday once.";
        }

        return condition.TimeZone is { } zone && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)
            ? null
            : "Choose the time zone the hours are in, by its IANA name, for example Europe/Berlin.";
    }

    #endregion
}
