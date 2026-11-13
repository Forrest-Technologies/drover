namespace Drover.Engine.Rules;

/// <summary>Rules as the API carries them, and requests read into Drover's own types.</summary>
public static class RuleViews
{
    #region Public Methods

    public static BotRuleResponse ToResponse(this InheritedRule found) =>
        new(
            found.Rule.Id,
            found.Rule.BotId,
            found.BotName,
            found.Distance > 0,
            found.Rule.Name,
            found.Rule.Enabled,
            found.Rule.Position,
            Words.Of(found.Rule.Checkpoint),
            Words.Of(found.Rule.Effect),
            [.. found.Rule.Conditions.Select(ToBody)]);

    public static RuleConditionBody ToBody(this RuleCondition condition) =>
        new(Words.Of(condition.Kind))
        {
            Tools = condition.Tools.Count == 0 ? null : condition.Tools,
            ToolKinds = condition.ToolKinds.Count == 0 ? null : [.. condition.ToolKinds.Select(Words.Of)],
            Argument = condition.Argument,
            Entries = condition.Entries.Count == 0 ? null : condition.Entries,
            Pattern = condition.Pattern,
            Builtin = condition.Builtin is { } builtin ? Words.Of(builtin) : null,
            From = condition.From,
            To = condition.To,
            Weekdays = condition.Weekdays.Count == 0 ? null : [.. condition.Weekdays.Select(Words.Of)],
            TimeZone = condition.TimeZone,
            Counter = condition.Counter is { } counter ? Words.Of(counter) : null,
            Limit = condition.Limit,
        };

    /// <summary>
    /// The condition a request describes, keeping only what its kind reads. A word that names nothing gives a value
    /// that is not defined, which the validator refuses in words; nothing is guessed.
    /// </summary>
    public static RuleCondition ToCondition(this RuleConditionBody body)
    {
        var kind = Words.Read<RuleConditionKind>(body.Kind) ?? (RuleConditionKind)(-1);
        var text = kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds or RuleConditionKind.NotInList;
        return new RuleCondition
        {
            Kind = kind,
            Tools = kind is RuleConditionKind.ToolIs ? [.. (body.Tools ?? []).Select(tool => tool?.Trim() ?? string.Empty)] : [],
            ToolKinds = kind is RuleConditionKind.ToolKind ? [.. (body.ToolKinds ?? []).Select(word => Words.Read<ToolSensitivity>(word) ?? (ToolSensitivity)(-1))] : [],
            Argument = text && !string.IsNullOrWhiteSpace(body.Argument) ? body.Argument.Trim() : null,
            Entries = kind is RuleConditionKind.TextContains or RuleConditionKind.NotInList ? [.. (body.Entries ?? []).Select(value => value ?? string.Empty)] : [],
            Pattern = kind is RuleConditionKind.TextMatches ? body.Pattern : null,
            Builtin = kind is RuleConditionKind.TextHolds ? Words.Read<BuiltinPattern>(body.Builtin) : null,
            From = kind is RuleConditionKind.TimeIs ? body.From : null,
            To = kind is RuleConditionKind.TimeIs ? body.To : null,
            Weekdays = kind is RuleConditionKind.TimeIs ? [.. (body.Weekdays ?? []).Select(word => Words.Read<DayOfWeek>(word) ?? (DayOfWeek)(-1))] : [],
            TimeZone = kind is RuleConditionKind.TimeIs ? body.TimeZone?.Trim() : null,
            Counter = kind is RuleConditionKind.RunExceeds ? Words.Read<RunCounter>(body.Counter) : null,
            Limit = kind is RuleConditionKind.RunExceeds ? body.Limit : null,
        };
    }

    #endregion
}
