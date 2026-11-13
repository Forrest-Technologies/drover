namespace Drover.Ui.Bots;

/// <summary>One condition while a person is writing it. Every field a condition kind can use is here; the kind says which are sent.</summary>
public sealed class ConditionDraft
{
    #region Private Fields

    private const string RecipientInputs = "to, cc, bcc";

    #endregion

    #region Properties

    public string Kind { get; set; } = "text_contains";

    public HashSet<string> Tools { get; } = [];

    public HashSet<string> ToolKinds { get; } = [];

    /// <summary>The inputs a list checks. It starts on the fields a recipient can be in.</summary>
    public string Argument { get; set; } = RecipientInputs;

    /// <summary>The words or list entries, one on each line.</summary>
    public string Entries { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public string Builtin { get; set; } = "card_number";

    public string From { get; set; } = "22:00";

    public string To { get; set; } = "07:00";

    public HashSet<string> Weekdays { get; } = [];

    public string TimeZone { get; set; } = string.Empty;

    public string Counter { get; set; } = "tool_calls";

    public string Limit { get; set; } = "20";

    #endregion

    #region Public Methods

    public static ConditionDraft Of(RuleConditionBody body, string timeZone)
    {
        var draft = new ConditionDraft
        {
            Kind = body.Kind is "not_in_list" && string.IsNullOrWhiteSpace(body.Argument) ? RuleWords.AddressesNotInList : body.Kind ?? "text_contains",
            Argument = body.Argument ?? RecipientInputs,
            Entries = string.Join('\n', body.Entries ?? []),
            Pattern = body.Pattern ?? string.Empty,
            Builtin = body.Builtin ?? "card_number",
            From = body.From ?? "22:00",
            To = body.To ?? "07:00",
            TimeZone = body.TimeZone ?? timeZone,
            Counter = body.Counter ?? "tool_calls",
            Limit = body.Limit?.ToString(CultureInfo.InvariantCulture) ?? "20",
        };
        draft.Tools.UnionWith(body.Tools ?? []);
        draft.ToolKinds.UnionWith(body.ToolKinds ?? []);
        draft.Weekdays.UnionWith(body.Weekdays ?? []);
        return draft;
    }

    /// <summary>The condition as it is sent: only what its kind uses.</summary>
    public RuleConditionBody ToBody()
    {
        string[] entries = [.. Entries.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        return Kind switch
        {
            "tool_is" => new RuleConditionBody(Kind) { Tools = [.. Tools.Order(StringComparer.Ordinal)] },
            "tool_kind" => new RuleConditionBody(Kind) { ToolKinds = [.. RuleWords.ToolKinds.Select(kind => kind.Value).Where(ToolKinds.Contains)] },
            "text_contains" => new RuleConditionBody(Kind) { Entries = entries },
            "text_holds" => new RuleConditionBody(Kind) { Builtin = Builtin },
            "text_matches" => new RuleConditionBody(Kind) { Pattern = Pattern },
            "not_in_list" => new RuleConditionBody(Kind) { Argument = Argument.Trim(), Entries = entries },

            // The server knows this as a list with no input named: every email address anywhere in the tool's input.
            RuleWords.AddressesNotInList => new RuleConditionBody("not_in_list") { Entries = entries },
            "time_is" => new RuleConditionBody(Kind)
            {
                From = Clock(From),
                To = Clock(To),
                Weekdays = Weekdays.Count == 0 ? null : [.. RuleWords.Weekdays.Select(day => day.Value).Where(Weekdays.Contains)],
                TimeZone = TimeZone.Trim(),
            },
            _ => new RuleConditionBody(Kind)
            {
                Counter = Counter,
                Limit = long.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : null,
            },
        };
    }

    #endregion

    #region Private Methods

    // A time field gives hours and minutes, and seconds too in some browsers. The server reads HH:mm.
    private static string Clock(string time) => time.Length > 5 ? time[..5] : time;

    #endregion
}

/// <summary>A rule while a person is writing it.</summary>
public sealed class RuleDraft
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Checkpoint { get; set; } = RuleWords.BeforeTool;

    public string Effect { get; set; } = "ask";

    public List<ConditionDraft> Conditions { get; } = [];

    #endregion

    #region Public Methods

    public static RuleDraft Of(BotRuleResponse rule, string timeZone)
    {
        var draft = new RuleDraft { Name = rule.Name, Enabled = rule.Enabled, Checkpoint = rule.Checkpoint, Effect = rule.Effect };
        draft.Conditions.AddRange(rule.Conditions.Select(condition => ConditionDraft.Of(condition, timeZone)));
        return draft;
    }

    public BotRuleBody ToBody() => new(Name.Trim(), Enabled, Checkpoint, Effect, [.. Conditions.Select(condition => condition.ToBody())]);

    /// <summary>
    /// Moves the rule to another checkpoint and drops what means nothing there: conditions that checkpoint does not
    /// have, and an effect it cannot carry out.
    /// </summary>
    public void MoveTo(string checkpoint)
    {
        Checkpoint = checkpoint;
        var allowed = RuleWords.Conditions(checkpoint).Select(choice => choice.Value).ToHashSet(StringComparer.Ordinal);
        Conditions.RemoveAll(condition => !allowed.Contains(condition.Kind));
        var effects = RuleWords.Effects(checkpoint);

        if (effects.All(choice => choice.Value != Effect))
        {
            Effect = effects[0].Value;
        }
    }

    #endregion
}

/// <summary>A rule offered ready-made. Choosing one adds it as written; it can be changed like any other afterwards.</summary>
public sealed record RuleStarter(string Label, IReadOnlyList<BotRuleBody> Rules);

/// <summary>The starters the editor offers.</summary>
public static class RuleStarters
{
    #region Public Methods

    /// <summary>The starters, with quiet hours in a person's own time zone.</summary>
    public static IReadOnlyList<RuleStarter> For(string timeZone) =>
    [
        new("Ask before anything that writes",
        [
            new BotRuleBody(
                "Ask before anything that writes",
                true,
                RuleWords.BeforeTool,
                "ask",
                [new RuleConditionBody("tool_kind") { ToolKinds = ["write", "critical"] }]),
        ]),
        new("Never send a card number or key",
        [
            new BotRuleBody("Never send a card number", true, RuleWords.BeforeTool, "block", [new RuleConditionBody("text_holds") { Builtin = "card_number" }]),
            new BotRuleBody("Never send a key", true, RuleWords.BeforeTool, "block", [new RuleConditionBody("text_holds") { Builtin = "key" }]),
        ]),
        new("Quiet hours",
        [
            new BotRuleBody(
                "Quiet hours",
                true,
                RuleWords.BeforeTool,
                "block",
                [
                    new RuleConditionBody("tool_kind") { ToolKinds = ["write", "critical"] },
                    new RuleConditionBody("time_is") { From = "22:00", To = "07:00", TimeZone = timeZone },
                ]),
        ]),
    ];

    #endregion
}
