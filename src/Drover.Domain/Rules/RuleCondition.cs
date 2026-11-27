namespace Drover.Domain.Rules;

/// <summary>
/// One condition of a rule. Which properties are used depends on <see cref="Kind"/>; the rest stay empty.
/// A condition is data and is decided by code. One kind is the exception, and only narrows:
/// <see cref="RuleConditionKind.Judged"/> holds a sentence that a decision model is asked about, and its
/// probabilities are compared with <see cref="Confidence"/> in code (ADR 0025).
/// </summary>
public sealed record RuleCondition
{
    #region Properties

    public RuleConditionKind Kind { get; init; }

    /// <summary>Tool names, for <see cref="RuleConditionKind.ToolIs"/>.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Sensitivities, for <see cref="RuleConditionKind.ToolKind"/>.</summary>
    public IReadOnlyList<ToolSensitivity> ToolKinds { get; init; } = [];

    /// <summary>
    /// The argument a text condition looks at, before a tool is used. Null looks at every piece of text there is.
    /// </summary>
    public string? Argument { get; init; }

    /// <summary>The pieces of text to find, or the list a value must be on.</summary>
    public IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>A regular expression, for <see cref="RuleConditionKind.TextMatches"/>.</summary>
    public string? Pattern { get; init; }

    public BuiltinPattern? Builtin { get; init; }

    /// <summary>The start of the time of day, as HH:mm. With <see cref="To"/> earlier, the span crosses midnight.</summary>
    public string? From { get; init; }

    public string? To { get; init; }

    /// <summary>The weekdays the condition holds on. Empty means every day.</summary>
    public IReadOnlyList<DayOfWeek> Weekdays { get; init; } = [];

    /// <summary>The IANA time zone the time of day is read in.</summary>
    public string? TimeZone { get; init; }

    public RunCounter? Counter { get; init; }

    public long? Limit { get; init; }

    /// <summary>The sentence a decision model judges, for <see cref="RuleConditionKind.Judged"/>.</summary>
    public string? Statement { get; init; }

    /// <summary>
    /// How sure the decision model must be, for <see cref="RuleConditionKind.Judged"/>: from 0.50 to 0.99. Null is
    /// <see cref="JudgedRules.DefaultConfidence"/>.
    /// </summary>
    public double? MinConfidence { get; init; }

    /// <summary>How sure the decision model must be for the rule to apply. At or below one minus this, it does not.</summary>
    public double Confidence => MinConfidence ?? JudgedRules.DefaultConfidence;

    public bool IsJudged => Kind is RuleConditionKind.Judged;

    /// <summary>
    /// Whether the condition looks for something in text. Such a condition catches the common forms and is not a
    /// guarantee. Every other condition is exact: decided on structure (which tool, its kind, the time, a count,
    /// the values of named inputs), and guaranteed.
    /// </summary>
    public bool IsPattern => Kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds
        || (Kind is RuleConditionKind.NotInList && Argument is null);

    #endregion
}
