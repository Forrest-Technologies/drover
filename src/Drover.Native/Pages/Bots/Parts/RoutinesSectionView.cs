namespace Drover.Native.Pages;

/// <summary>
/// The Routines section of the bot editor. Its job: decide what this bot does on its own and when, and check that
/// it did.
/// </summary>
public sealed class RoutinesSectionView : EditorPart
{
    #region Private Fields

    private readonly TextBlock loading = BotParts.Hint(RoutinesSectionViewModel.LoadingRoutines);

    private readonly HiddenStatus announcement = new();

    private readonly NamedGroup list = new(RoutinesSectionViewModel.ListName, "DvSpace3");

    private readonly TextBlock empty = BotParts.Hint();

    private readonly ContentControl form = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };

    private readonly StackPanel addArea = new();

    private readonly Button add;

    private readonly Dialog runDialog = new() { Title = RoutinesSectionViewModel.RunTitle };

    private readonly TextBlock runWords = Styles.Text(null, "DvTextParagraph");

    private readonly TextBlock runPrompt = Styles.Text(null, "DvTextParagraph");

    private readonly Button runConfirm;

    private readonly Dialog removeDialog = new() { Title = RoutinesSectionViewModel.RemoveTitle };

    private readonly TextBlock removingWords = Styles.Text(null, "DvTextParagraph");

    private readonly Button removeConfirm;

    private readonly Dictionary<Guid, RoutineRowView> rows = [];

    private RoutineForm? shownForm;

    private bool showing;

    #endregion

    #region Constructors

    public RoutinesSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        var part = editor.RoutinesPart;
        AutomationProperties.SetLiveSetting(loading, AutomationLiveSetting.Polite);
        add = BotParts.IconAction(IconName.Plus, RoutinesSectionViewModel.AddRoutine, part.Add);
        addArea.Children.Add(add);
        runConfirm = BotParts.Action(RoutinesSectionViewModel.RunConfirm, () => _ = part.RunConfirmed(), ButtonKind.Primary);
        removeConfirm = BotParts.Action(RoutinesSectionViewModel.RemoveRoutine, () => _ = part.Remove(), ButtonKind.Danger);
        var promptBox = new Border { Child = runPrompt, Padding = new(Tokens.Number("DvSpace3")), BorderThickness = new(Tokens.Number("DvBorder")) };
        promptBox.WhenShownOrRethemed(() => promptBox.BorderBrush = Tokens.Brush(promptBox, "DvLine"));
        runDialog.Body = BotParts.Column("DvSpace3", runWords, promptBox);
        runDialog.Actions = Actions(BotParts.Action(RoutinesSectionViewModel.DoNotRun, () => part.SetConfirmingRun(false)), runConfirm);
        removeDialog.Body = BotParts.Column("DvSpace3", removingWords, Styles.Text(RoutinesSectionViewModel.RemoveWords, "DvTextParagraph"));
        removeDialog.Actions = Actions(BotParts.Action(RoutinesSectionViewModel.KeepRoutine, () => part.SetConfirming(false)), removeConfirm);
        runDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => Closed(runDialog, part.SetConfirmingRun));
        removeDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => Closed(removeDialog, part.SetConfirming));

        foreach (var child in new UIElement[] { loading, list, empty, form, addArea })
        {
            Parts.Add(child);
        }

        Beside(announcement);
        Beside(runDialog);
        Beside(removeDialog);
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var part = Editor.RoutinesPart;
        showing = true;
        loading.Shown(part.Loading);
        announcement.Say(part.Announcement);
        ShowRows(part);
        empty.Shown(!part.Loading && part.EmptyWords is not null).Show(part.EmptyWords);
        ShowForm(part);
        addArea.Shown(!part.Loading && part.AddShown);
        runWords.Show(part.RunWords);
        runPrompt.Show(part.Running?.Prompt);
        runConfirm.IsEnabled = removeConfirm.IsEnabled = !part.Busy;
        removingWords.Show(part.RemovingWords);
        runDialog.Open = part.ConfirmingRun;
        removeDialog.Open = part.Confirming;
        showing = false;
        MoveFocus(part);
    }

    private void ShowRows(RoutinesSectionViewModel part)
    {
        List<UIElement> shown = [];

        foreach (var schedule in part.Loading ? [] : part.Schedules)
        {
            if (!rows.TryGetValue(schedule.Id, out var row))
            {
                row = new RoutineRowView(part, schedule);
                rows[schedule.Id] = row;
            }

            row.Show(schedule);
            shown.Add(row);
        }

        list.Shown(shown.Count > 0);
        if (!list.Children.SequenceEqual(shown))
        {
            list.Fill(shown);
        }
    }

    // The form is made each time it opens, for a new routine or the one being changed, and kept while it is open.
    private void ShowForm(RoutinesSectionViewModel part)
    {
        if (!part.Editing)
        {
            shownForm = null;
        }
        else if (shownForm is null || shownForm.EditedId != part.EditedId)
        {
            shownForm = new RoutineForm(part);
        }

        if (!ReferenceEquals(form.Content, shownForm))
        {
            form.Content = shownForm;
        }

        form.Shown(shownForm is not null);
        shownForm?.Show();
    }

    // Focus goes where the control a person used went: into the form that opened, or back to the add button.
    private void MoveFocus(RoutinesSectionViewModel part)
    {
        if (part.TakeFocus() is { } target)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (target is RoutineFocus.Form)
                {
                    shownForm?.TakeFocus();
                }
                else
                {
                    add.TakeFocus();
                }
            });
        }
    }

    private void Closed(Dialog dialog, Action<bool> close)
    {
        if (!showing && !dialog.Open)
        {
            close(false);
        }
    }

    private static StackPanel Actions(params UIElement[] buttons)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        foreach (var button in buttons)
        {
            actions.Children.Add(button);
        }

        return actions;
    }

    #endregion
}
