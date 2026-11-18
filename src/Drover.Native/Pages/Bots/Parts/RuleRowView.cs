namespace Drover.Native.Pages;

/// <summary>
/// One rule in the Rules section: the rule as its sentence, its name, and what the server notes about it. A rule of
/// the bot's own has its switch and the buttons that change or remove it; one from a bot above has none.
/// </summary>
public sealed class RuleRowView : Border
{
    #region Private Fields

    private readonly TextBlock sentence = Styles.Text(null);

    private readonly TextBlock name = BotParts.Hint();

    private readonly StackPanel notes = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly Switch on = new() { Label = RulesSectionViewModel.SwitchLabel };

    private readonly IconButton change;

    private readonly IconButton remove;

    private BotRuleResponse rule;

    private bool showing;

    #endregion

    #region Constructors

    public RuleRowView(RulesSectionViewModel part, BotRuleResponse rule)
    {
        this.rule = rule;
        change = new IconButton { Icon = IconName.Edit, Label = RulesSectionViewModel.ChangeLabel(rule) };
        remove = new IconButton { Icon = IconName.Remove, Label = RulesSectionViewModel.RemoveLabel(rule) };
        sentence.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var text = BotParts.Column("DvSpace1", sentence, name, notes);
        var row = new WrapRow();
        row.Children.Add(text);

        if (!rule.Inherited)
        {
            on.OnChanged(() => showing, value => _ = part.SwitchRule(this.rule, value));
            change.Click += (_, _) => part.Edit(this.rule);
            remove.Click += (_, _) => _ = part.Remove(this.rule);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
            actions.Children.Add(on);
            actions.Children.Add(change);
            actions.Children.Add(remove);
            row.Children.Add(actions);
        }

        Child = row;
        Padding = new(0, Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"));
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        this.WhenShownOrRethemed(() => BorderBrush = Tokens.Brush(this, "DvLine"));
    }

    #endregion

    #region Public Methods

    public void Show(BotRuleResponse shown, bool busy)
    {
        showing = true;
        rule = shown;
        sentence.Show(RuleWords.Sentence(shown));
        name.Show(shown.Inherited ? RulesSectionViewModel.InheritedLine(shown) : shown.Name);

        if (!notes.Children.OfType<TextBlock>().Select(note => note.Text).SequenceEqual(shown.Notes))
        {
            notes.Fill(shown.Notes.Select(note => (UIElement)BotParts.Small(note, "DvWarn")));
        }

        notes.Shown(shown.Notes.Count > 0);
        on.About = shown.Name;
        on.Show(shown.Enabled);
        on.IsEnabled = change.IsEnabled = remove.IsEnabled = !busy;
        change.Label = RulesSectionViewModel.ChangeLabel(shown);
        remove.Label = RulesSectionViewModel.RemoveLabel(shown);
        showing = false;
    }

    #endregion
}
