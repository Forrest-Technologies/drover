namespace Drover.Domain.Rules;

/// <summary>
/// A rule as the evaluator takes it: checked, switched on, and in its order among the bot's rules, its ancestors'
/// first. <see cref="FromBotId"/> is the bot the rule was written on.
/// </summary>
public sealed record ActiveRule(
    Guid Id,
    string Name,
    RuleCheckpoint Checkpoint,
    RuleEffect Effect,
    IReadOnlyList<RuleCondition> Conditions,
    Guid FromBotId = default)
{
    #region Properties

    /// <summary>
    /// The server does not run patterns a person wrote (deployment rule AllowWrittenRulePatterns). A condition of
    /// this rule that has one is not run: it counts as not checked, so the rule takes its effect.
    /// </summary>
    public bool WrittenPatternsOff { get; init; }

    #endregion
}

/// <summary>
/// What a rule is checked against. Only what the checkpoint has is set: a tool and its arguments before a tool, its
/// result after, and the text for a reply or a message.
/// </summary>
public sealed record RuleSubject
{
    #region Properties

    public required RuleCheckpoint Checkpoint { get; init; }

    public string? ToolName { get; init; }

    public ToolSensitivity? ToolKind { get; init; }

    /// <summary>A tool's arguments, or its result.</summary>
    public JsonElement? Json { get; init; }

    /// <summary>A reply, or a person's message.</summary>
    public string? Text { get; init; }

    public DateTimeOffset Now { get; init; }

    public int ToolCalls { get; init; }

    public long Tokens { get; init; }

    #endregion
}

/// <summary>
/// The rule that decides, and what it does. For a redaction, the subject with every match replaced is carried
/// here; nothing in this record holds the text that matched.
/// </summary>
public sealed record RuleOutcome(ActiveRule Rule, RuleEffect Effect, string Why)
{
    #region Properties

    public JsonElement? RedactedJson { get; init; }

    public string? RedactedText { get; init; }

    /// <summary>Every rule that applied, the deciding one first.</summary>
    public IReadOnlyList<ActiveRule> Applied { get; init; } = [];

    #endregion
}
