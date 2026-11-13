namespace Drover.Ui.Bots;

/// <summary>One choice in the rule builder: the word the server knows it by, and the words a person reads.</summary>
public sealed record RuleChoice(string Value, string Label);

/// <summary>
/// Rules in words. A rule is built as "when, if, then" and read back as one sentence; this is where the builder's
/// choices and that sentence come from, so the screen and a screen reader say the same thing.
/// </summary>
public static class RuleWords
{
    #region Private Fields

    private static readonly RuleChoice[] checkpoints =
    [
        new(BeforeTool, "A tool is about to run"),
        new(AfterTool, "A tool has answered"),
        new(BeforeReply, "The bot is about to reply"),
        new(MessageArrives, "A message arrives"),
    ];

    private static readonly RuleChoice[] toolKinds = [new("read", "reads"), new("write", "writes"), new("critical", "is critical")];

    private static readonly RuleChoice[] builtins = [new("email_address", "an email address"), new("card_number", "a card number"), new("key", "a key or token")];

    private static readonly RuleChoice[] counters = [new("tool_calls", "tool calls"), new("tokens", "tokens")];

    private static readonly RuleChoice[] weekdays =
    [
        new("monday", "Monday"),
        new("tuesday", "Tuesday"),
        new("wednesday", "Wednesday"),
        new("thursday", "Thursday"),
        new("friday", "Friday"),
        new("saturday", "Saturday"),
        new("sunday", "Sunday"),
    ];

    #endregion

    #region Properties

    /// <summary>What a condition on text does not find, said where a person writes one.</summary>
    public static string NotCaught =>
        "Characters that cannot be seen are ignored. Not caught: a letter from another alphabet that only looks the same, text split across two inputs or two items of a list, and a card number with other digits joined to it without a gap.";

    public static IReadOnlyList<RuleChoice> Checkpoints => checkpoints;

    public static IReadOnlyList<RuleChoice> ToolKinds => toolKinds;

    public static IReadOnlyList<RuleChoice> Builtins => builtins;

    public static IReadOnlyList<RuleChoice> Counters => counters;

    public static IReadOnlyList<RuleChoice> Weekdays => weekdays;

    #endregion

    #region Public Methods

    /// <summary>What a rule can do at a checkpoint. Asking exists only before a tool; a person's message is never rewritten.</summary>
    public static IReadOnlyList<RuleChoice> Effects(string checkpoint) =>
    [
        .. checkpoint is BeforeTool ? [new RuleChoice("ask", "Ask a person first")] : Array.Empty<RuleChoice>(),
        new("block", Capitalized(Then(checkpoint, "block"))),
        .. checkpoint is MessageArrives ? [] : new[] { new RuleChoice("redact", "Remove what matched") },
        new("stop", "Stop the run"),
    ];

    /// <summary>The conditions that mean something at a checkpoint.</summary>
    public static IReadOnlyList<RuleChoice> Conditions(string checkpoint) =>
    [
        .. checkpoint is BeforeTool or AfterTool
            ? [new RuleChoice("tool_is", "The tool is one of"), new RuleChoice("tool_kind", "The tool is of a kind")]
            : Array.Empty<RuleChoice>(),
        new("text_contains", $"{Capitalized(Subject(checkpoint))} contains"),
        new("text_holds", $"{Capitalized(Subject(checkpoint))} holds"),
        new("text_matches", $"{Capitalized(Subject(checkpoint))} matches a pattern"),
        .. checkpoint is BeforeTool ? [new RuleChoice("not_in_list", "An address or an input is not on a list")] : Array.Empty<RuleChoice>(),
        new("time_is", "The time is between"),
        new("run_exceeds", "The run has used more than"),
    ];

    /// <summary>The whole rule as one sentence: "When a tool is about to run, if the tool writes, then ask a person first."</summary>
    public static string Sentence(string checkpoint, string effect, IReadOnlyList<RuleConditionBody> conditions)
    {
        var when = (checkpoints.FirstOrDefault(choice => choice.Value == checkpoint)?.Label ?? checkpoint).ToLowerInvariant();
        var holds = string.Join(" and ", conditions.Select(condition => Condition(checkpoint, condition)));
        return conditions.Count == 0 ? $"When {when}, {Then(checkpoint, effect)}." : $"When {when}, if {holds}, then {Then(checkpoint, effect)}.";
    }

    public static string Sentence(BotRuleResponse rule) => Sentence(rule.Checkpoint, rule.Effect, rule.Conditions);

    /// <summary>The section's one line while it is closed: "No rules", or how many and how many of them are off.</summary>
    public static string Summary(IReadOnlyList<BotRuleResponse>? rules)
    {
        if (rules is null)
        {
            return "Loading";
        }

        if (rules.Count == 0)
        {
            return "No rules";
        }

        var off = rules.Count(rule => !rule.Enabled);
        var count = rules.Count == 1 ? "1 rule" : $"{rules.Count} rules";
        return off == 0 ? count : $"{count}, {off} off";
    }

    /// <summary>What a trial found, as a sentence. The server decided; this only words it.</summary>
    public static string Trial(TryRulesResponse found) =>
        found.Effect is null || found.RuleName is null
            ? "No rule applies to this sample."
            : $"The rule \"{found.RuleName}\" would {Would(found.Effect)}: {found.Why}.";

    #endregion

    #region Private Methods

    private static string Subject(string checkpoint) => checkpoint switch
    {
        BeforeTool => "the tool's input",
        AfterTool => "the tool's answer",
        BeforeReply => "the reply",
        _ => "the message",
    };

    private static string Then(string checkpoint, string effect) => effect switch
    {
        "redact" => "remove what matched",
        "ask" => "ask a person first",
        "stop" => "stop the run",
        _ => checkpoint switch
        {
            BeforeTool => "do not run it",
            AfterTool => "keep the answer from the bot",
            BeforeReply => "do not send it",
            _ => "do not give it to the bot",
        },
    };

    private static string Would(string effect) => effect switch
    {
        "redact" => "remove what matched",
        "ask" => "ask a person first",
        "stop" => "stop the run",
        _ => "block this",
    };

    private static string Condition(string checkpoint, RuleConditionBody condition)
    {
        var subject = string.IsNullOrWhiteSpace(condition.Argument) ? Subject(checkpoint) : $"the input \"{condition.Argument}\"";
        return condition.Kind switch
        {
            "tool_is" => $"the tool is {Either(condition.Tools ?? [])}",
            "tool_kind" => $"the tool {Either([.. (condition.ToolKinds ?? []).Select(kind => Label(toolKinds, kind))])}",
            "text_contains" => $"{subject} contains {Either([.. (condition.Entries ?? []).Select(entry => $"\"{entry}\"")])}",
            "text_holds" => $"{subject} holds {Label(builtins, condition.Builtin)}",
            "text_matches" => $"{subject} matches the pattern {condition.Pattern}",
            "not_in_list" => $"{Inputs(condition.Argument)} not one of {string.Join(", ", condition.Entries ?? [])}",
            "time_is" => Time(condition),
            "run_exceeds" => $"the run has used more than {condition.Limit?.ToString("N0", CultureInfo.InvariantCulture)} {Label(counters, condition.Counter)}",
            _ => condition.Kind ?? string.Empty,
        };
    }

    // A list rule names one input or several, parted by commas.
    private static string Inputs(string? argument)
    {
        string[] names = [.. (argument ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(name => $"\"{name}\"")];
        return names.Length switch
        {
            0 => "an email address anywhere in the tool's input is",
            1 => $"the input {names[0]} is",
            _ => $"any of the inputs {string.Join(", ", names)} is",
        };
    }

    private static string Time(RuleConditionBody condition)
    {
        var days = condition.Weekdays is { Count: > 0 } named ? $" on {string.Join(", ", named.Select(day => Label(weekdays, day)))}" : string.Empty;
        return $"it is between {condition.From} and {condition.To}{days} ({condition.TimeZone})";
    }

    // A condition still being written has nothing chosen yet; the sentence shows the gap.
    private static string Either(IReadOnlyList<string> words) => words.Count == 0 ? "…" : string.Join(" or ", words);

    private static string Label(IReadOnlyList<RuleChoice> choices, string? value) =>
        choices.FirstOrDefault(choice => choice.Value == value)?.Label ?? value ?? string.Empty;

    private static string Capitalized(string words) => words.Length == 0 ? words : $"{char.ToUpperInvariant(words[0])}{words[1..]}";

    #endregion

    #region Checkpoints

    public const string BeforeTool = "before_tool";

    public const string AfterTool = "after_tool";

    public const string BeforeReply = "before_reply";

    public const string MessageArrives = "message_arrives";

    #endregion
}
