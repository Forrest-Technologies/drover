namespace Drover.Native.Pages;

/// <summary>
/// One condition of the rule being written: what kind it is, then only the fields that kind has, with what a
/// condition on text does not find said beside it.
/// </summary>
public sealed class RuleConditionView : Border
{
    #region Private Fields

    private readonly RulesSectionViewModel part;

    private readonly NamedGroup group;

    private readonly Select kind;

    private readonly IconButton remove;

    private bool showing;

    #endregion

    #region Constructors

    public RuleConditionView(RulesSectionViewModel part, ConditionDraft condition)
    {
        (this.part, Condition) = (part, condition);
        Kind = condition.Kind;
        kind = new Select().OnChanged(() => showing, value => part.Change(() => condition.Kind = value));
        remove = new IconButton { Icon = IconName.Remove, Label = RulesSectionViewModel.RemoveConditionLabel(1), VerticalAlignment = VerticalAlignment.Bottom };
        remove.Click += (_, _) => part.RemoveCondition(condition);
        var head = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(remove, 1);
        head.Children.Add(kind);
        head.Children.Add(remove);
        group = new NamedGroup(RulesSectionViewModel.ConditionName(1), "DvSpace3", [head, .. Fields()]);
        Child = group;
        Padding = new(0, Tokens.Number("DvSpace3"), 0, 0);
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        this.WhenShownOrRethemed(() => BorderBrush = Tokens.Brush(this, "DvLine"));
    }

    #endregion

    #region Properties

    public ConditionDraft Condition { get; }

    /// <summary>The kind the fields were built for. A condition that changes kind gets a view of its new kind.</summary>
    public string Kind { get; }

    #endregion

    #region Public Methods

    public void Show(int number)
    {
        showing = true;
        group.Describe(RulesSectionViewModel.ConditionName(number));
        kind.Label = RulesSectionViewModel.IfLabel(number);
        kind.Show([.. part.ConditionChoices(Condition).Select(choice => new ChoiceOption(choice.Value, choice.Label))], Condition.Kind);
        remove.Label = RulesSectionViewModel.RemoveConditionLabel(number);
        showing = false;
    }

    #endregion

    #region Private Methods

    private IEnumerable<UIElement> Fields() => Kind switch
    {
        "tool_is" => ToolIs(),
        "tool_kind" => [Checks(RulesSectionViewModel.ToolKindLegend, RuleWords.ToolKinds, Condition.ToolKinds)],
        "text_contains" => [Entries(RulesSectionViewModel.EntriesLabel, null), BotParts.Hint(RuleWords.NotCaught)],
        "text_holds" => [Choice(RulesSectionViewModel.WhatLabel, RuleWords.Builtins, Condition.Builtin, value => Condition.Builtin = value), BotParts.Hint(RuleWords.NotCaughtHeld)],
        "text_matches" =>
        [
            Text(RulesSectionViewModel.PatternLabel, Condition.Pattern, RulesSectionViewModel.PatternHint, value => Condition.Pattern = value),
            BotParts.Hint(RuleWords.NotCaught),
        ],
        "not_in_list" =>
        [
            Text(RulesSectionViewModel.InputNamesLabel, Condition.Argument, RulesSectionViewModel.InputNamesHint, value => Condition.Argument = value),
            BotParts.Hint(RuleWords.ListIsExact),
            Entries(RulesSectionViewModel.AllowedLabel, RulesSectionViewModel.AllowedHint),
        ],
        RuleWords.AddressesNotInList => [Entries(RulesSectionViewModel.AllowedLabel, RulesSectionViewModel.AddressesHint), BotParts.Hint(RuleWords.NotCaughtAddresses)],
        "time_is" => TimeIs(),
        _ => [Pair(
            Text(RulesSectionViewModel.MoreThanLabel, Condition.Limit, null, value => Condition.Limit = value),
            Choice(RulesSectionViewModel.OfLabel, RuleWords.Counters, Condition.Counter, value => Condition.Counter = value))],
    };

    private IEnumerable<UIElement> ToolIs() =>
        part.ToolNames.Count == 0
            ? [BotParts.Hint(RulesSectionViewModel.NoTools)]
            : [Checks(RulesSectionViewModel.ToolsLegend, [.. part.ToolNames.Select(tool => new RuleChoice(tool, tool))], Condition.Tools, machine: true)];

    private IEnumerable<UIElement> TimeIs()
    {
        var days = new FlowPanel { Gap = Tokens.Number("DvSpace4") };
        foreach (var weekday in RuleWords.Weekdays)
        {
            days.Children.Add(Check(weekday, Condition.Weekdays, machine: false));
        }

        return
        [
            Pair(
                Text(RulesSectionViewModel.FromLabel, Condition.From, null, value => Condition.From = value),
                Text(RulesSectionViewModel.ToLabel, Condition.To, null, value => Condition.To = value)),
            Text(RulesSectionViewModel.TimeZoneLabel, Condition.TimeZone, RulesSectionViewModel.TimeZoneHint, value => Condition.TimeZone = value),
            new NamedGroup(RulesSectionViewModel.DaysLegend, "DvSpace3", BotParts.ChoiceLabel(RulesSectionViewModel.DaysLegend), days),
        ];
    }

    private NamedGroup Checks(string legend, IReadOnlyList<RuleChoice> choices, HashSet<string> chosen, bool machine = false) =>
        new(legend, "DvSpace3", [BotParts.ChoiceLabel(legend), BotParts.Column("DvSpace1", [.. choices.Select(choice => Check(choice, chosen, machine))])]);

    private CheckItem Check(RuleChoice choice, HashSet<string> chosen, bool machine)
    {
        var box = new CheckItem(choice.Label, machine);
        box.Show(chosen.Contains(choice.Value));
        box.Changed += on => part.Check(chosen, choice.Value, on);
        return box;
    }

    private TextArea Entries(string label, string? hint) =>
        new TextArea { Label = label, Rows = 2, Hint = hint, Value = Condition.Entries }.OnChanged(() => showing, value => part.Change(() => Condition.Entries = value));

    private Field Text(string label, string value, string? hint, Action<string> write) =>
        new Field { Label = label, Hint = hint, Value = value }.OnChanged(() => showing, text => part.Change(() => write(text)));

    private Select Choice(string label, IReadOnlyList<RuleChoice> choices, string value, Action<string> write)
    {
        var select = new Select { Label = label };
        select.Show([.. choices.Select(choice => new ChoiceOption(choice.Value, choice.Label))], value);
        return select.OnChanged(() => showing, chosen => part.Change(() => write(chosen)));
    }

    private static PairPanel Pair(UIElement first, UIElement second)
    {
        var pair = new PairPanel();
        pair.Children.Add(first);
        pair.Children.Add(second);
        return pair;
    }

    #endregion
}
