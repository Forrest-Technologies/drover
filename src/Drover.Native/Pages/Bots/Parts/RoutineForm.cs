namespace Drover.Native.Pages;

/// <summary>
/// A routine being written or changed: what to ask the bot, when, and where it answers. A new routine is saved off,
/// and the form says so.
/// </summary>
public sealed class RoutineForm : NamedGroup
{
    #region Private Fields

    private readonly RoutinesSectionViewModel part;

    private readonly TextBlock legend = BotParts.ChoiceLabel(string.Empty);

    private readonly TextArea prompt;

    private readonly RoutineWhenView when;

    private readonly SegmentedControl where;

    private readonly TextBlock savedOff = BotParts.Hint(RoutinesSectionViewModel.SavedOff);

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Button cancel;

    private readonly Button save;

    private bool showing;

    #endregion

    #region Constructors

    public RoutineForm(RoutinesSectionViewModel part)
        : base(string.Empty, "DvSpace3")
    {
        this.part = part;
        EditedId = part.EditedId;
        prompt = new TextArea { Rows = 3, Value = part.Prompt }.OnChanged(() => showing, value => part.Prompt = value);
        when = new RoutineWhenView(part);
        where = new SegmentedControl { Label = RoutinesSectionViewModel.WhereLabel }.OnChanged(() => showing, value => part.ThreadMode = value);
        cancel = BotParts.Action(RoutinesSectionViewModel.Cancel, part.CancelForm);
        save = BotParts.Action(string.Empty, () => _ = part.Save(), ButtonKind.Primary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel);
        actions.Children.Add(save);

        foreach (var child in new UIElement[]
        {
            legend, prompt, when, BotParts.Column("DvSpace1", BotParts.ChoiceLabel(RoutinesSectionViewModel.WhereLabel), where), savedOff, problem, actions,
        })
        {
            Children.Add(child);
        }
    }

    #endregion

    #region Properties

    /// <summary>The routine this form changes, or null for a new one.</summary>
    public Guid? EditedId { get; }

    #endregion

    #region Public Methods

    public void Show()
    {
        showing = true;
        legend.Show(part.FormLegend);
        Describe(part.FormLegend);
        prompt.Label = part.PromptLabel;
        when.Show();
        where.Show(RoutinesSectionViewModel.ThreadModes, part.ThreadMode);
        savedOff.Shown(part.SavedOffShown);
        problem.Show(part.FormProblem);
        cancel.IsEnabled = save.IsEnabled = !part.Busy;
        save.Content = part.SaveLabel;
        showing = false;
    }

    /// <summary>Takes the focus when the form opens in place of the button that opened it, so it is read out with its name.</summary>
    public void TakeFocus() => prompt.TakeFocus();

    #endregion
}
