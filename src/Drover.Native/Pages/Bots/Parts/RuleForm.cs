namespace Drover.Native.Pages;

/// <summary>
/// The rule being written or changed: when, the conditions, then what, its name and whether it is on, and the rule
/// read back as the sentence it will be listed as.
/// </summary>
public sealed class RuleForm : NamedGroup
{
    #region Private Fields

    private readonly RulesSectionViewModel part;

    private readonly RuleDraft draft;

    private readonly TextBlock legend = BotParts.ChoiceLabel(string.Empty);

    private readonly Select when;

    private readonly StackPanel conditions = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly TextBlock addConditionWords = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly Button addCondition;

    private readonly Select then;

    private readonly Field name;

    private readonly Switch on;

    private readonly TextBlock sentence = Styles.Text(null, "DvTextParagraph");

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Button cancel;

    private readonly Button save;

    private readonly Dictionary<ConditionDraft, RuleConditionView> views = [];

    private bool showing;

    #endregion

    #region Constructors

    public RuleForm(RulesSectionViewModel part, RuleDraft draft)
        : base(string.Empty, "DvSpace3")
    {
        (this.part, this.draft) = (part, draft);
        when = new Select { Label = RulesSectionViewModel.WhenLabel }.OnChanged(() => showing, part.MoveTo);
        addCondition = AddConditionButton();
        then = new Select { Label = RulesSectionViewModel.ThenLabel }.OnChanged(() => showing, value => part.Change(() => draft.Effect = value));
        name = new Field { Label = RulesSectionViewModel.NameLabel, Hint = RulesSectionViewModel.NameHint }.OnChanged(() => showing, value => part.Change(() => draft.Name = value));
        on = new Switch { Label = RulesSectionViewModel.SwitchLabel }.OnChanged(() => showing, value => part.Change(() => draft.Enabled = value));
        cancel = BotParts.Action(RulesSectionViewModel.Cancel, part.CancelDraft);
        save = BotParts.Action(string.Empty, () => _ = part.Save(), ButtonKind.Primary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace1") };
        actions.Children.Add(cancel);
        actions.Children.Add(save);

        foreach (var child in new UIElement[] { legend, when, conditions, addCondition, then, name, on, Preview(), problem, actions })
        {
            Children.Add(child);
        }
    }

    #endregion

    #region Public Methods

    public void Show()
    {
        showing = true;
        legend.Show(part.FormLegend);
        Describe(part.FormLegend);
        when.Show([.. RuleWords.Checkpoints.Select(choice => new ChoiceOption(choice.Value, choice.Label))], draft.Checkpoint);
        ShowConditions();
        addConditionWords.Show(part.AddConditionLabel);
        AutomationProperties.SetName(addCondition, part.AddConditionLabel);
        then.Show([.. part.Effects.Select(choice => new ChoiceOption(choice.Value, choice.Label))], draft.Effect);
        name.Show(draft.Name);
        on.Show(draft.Enabled);
        sentence.Text = part.Preview;
        problem.Show(part.Problem);
        cancel.IsEnabled = save.IsEnabled = !part.Busy;
        save.Content = part.SaveLabel;
        showing = false;
    }

    #endregion

    #region Private Methods

    // A condition keeps its view while it keeps its kind; one that changes kind is shown with the new kind's fields.
    private void ShowConditions()
    {
        List<UIElement> shown = [];

        foreach (var (condition, index) in draft.Conditions.Select((condition, index) => (condition, index)))
        {
            if (!views.TryGetValue(condition, out var view) || view.Kind != condition.Kind)
            {
                view = new RuleConditionView(part, condition);
                views[condition] = view;
            }

            view.Show(index + 1);
            shown.Add(view);
        }

        if (!conditions.Children.SequenceEqual(shown))
        {
            conditions.Fill(shown);
        }

        conditions.Shown(shown.Count > 0);
    }

    private Button AddConditionButton()
    {
        var mark = new Icon { NameOfIcon = IconName.Plus, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(mark);
        content.Children.Add(addConditionWords);
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
        button.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => mark.Foreground = button.Foreground);
        button.Click += (_, _) => part.AddCondition();
        return button;
    }

    // "Reads as", in the quieter style of a name and on a line of its own, then the whole rule as one sentence.
    private StackPanel Preview() => BotParts.Column(null, BotParts.Hint(RulesSectionViewModel.ReadsAs), sentence);

    #endregion
}
