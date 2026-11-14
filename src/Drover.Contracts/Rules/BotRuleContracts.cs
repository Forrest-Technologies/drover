namespace Drover.Contracts.Rules;

/// <summary>
/// One condition of a rule. The kind says which of the other parts are read: "tool_is" (tools), "tool_kind"
/// (tool kinds: "read", "write", "critical"), "text_contains" (entries), "text_matches" (pattern), "not_in_list"
/// (argument and entries), "text_holds" (builtin: "email_address", "card_number", "key"), "time_is" (from and to as
/// HH:mm, weekdays, time zone), "run_exceeds" (counter: "tool_calls" or "tokens", and limit).
/// </summary>
public sealed record RuleConditionBody(string? Kind)
{
    #region Properties

    public IReadOnlyList<string>? Tools { get; init; }

    public IReadOnlyList<string>? ToolKinds { get; init; }

    public string? Argument { get; init; }

    public IReadOnlyList<string>? Entries { get; init; }

    public string? Pattern { get; init; }

    public string? Builtin { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public IReadOnlyList<string>? Weekdays { get; init; }

    public string? TimeZone { get; init; }

    public string? Counter { get; init; }

    public long? Limit { get; init; }

    #endregion
}

/// <summary>
/// A rule as a person writes it. The checkpoint is "before_tool", "after_tool", "before_reply", or
/// "message_arrives"; the effect is "redact", "ask", "block", or "stop". Position is the rule's place among the
/// bot's own rules, from zero; left out, a new rule goes last and a changed one stays where it is.
/// </summary>
public sealed record BotRuleBody(string? Name, bool Enabled, string? Checkpoint, string? Effect, IReadOnlyList<RuleConditionBody>? Conditions, int? Position = null);

/// <summary>
/// A rule a bot follows. An inherited rule was written on a bot above this one, which is named, and cannot be
/// changed from here.
/// </summary>
public sealed record BotRuleResponse(
    Guid Id,
    Guid BotId,
    string BotName,
    bool Inherited,
    string Name,
    bool Enabled,
    int Position,
    string Checkpoint,
    string Effect,
    IReadOnlyList<RuleConditionBody> Conditions)
{
    #region Properties

    /// <summary>
    /// What a person should know about the rule as things stand now, in sentences: a tool it names that no tool is
    /// called, or an input it checks without saying for which tools. Empty when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    #endregion
}

/// <summary>
/// A sample to try a bot's rules on: a tool with its arguments or its result as JSON text, or a reply or message as
/// text. A draft is a rule that is being written and not saved; it is tried as if it were the bot's last rule.
/// </summary>
public sealed record TryRulesRequest(string? Checkpoint, string? Tool, string? Json, string? Text, BotRuleBody? Draft = null);

/// <summary>
/// What a run would do with the sample. The effect is null when no rule applies. The redacted fields hold the
/// sample with every match replaced, when the deciding effect is a redaction.
/// </summary>
public sealed record TryRulesResponse(
    string? Effect,
    Guid? RuleId,
    string? RuleName,
    string? Why,
    string? RedactedText,
    string? RedactedJson,
    IReadOnlyList<string> Applied)
{
    #region Properties

    /// <summary>
    /// Whether any rule tried at this moment looks for something in text, whether or not a rule applied. Such a
    /// condition catches the common forms and is not a guarantee, so "no rule applies" is not proof that nothing is
    /// there, and the editor says so beside the answer.
    /// </summary>
    public bool CatchesCommonForms { get; init; }

    #endregion
}
