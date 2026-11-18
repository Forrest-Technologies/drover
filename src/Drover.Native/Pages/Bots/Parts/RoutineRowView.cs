namespace Drover.Native.Pages;

/// <summary>
/// One routine: when it runs as a sentence, what it is sent, when it runs next, how the last run went, and its
/// controls. The sentence is the row's heading, so it is what is read out first.
/// </summary>
public sealed class RoutineRowView : Grid
{
    #region Private Fields

    private readonly RoutinesSectionViewModel part;

    private readonly Facet frame = new();

    private readonly TextBlock sentence = Styles.Text(null);

    private readonly TextBlock cron = BotParts.Mono();

    private readonly TextBlock prompt = Styles.Text(null, "DvTextParagraph").Inked("DvText2");

    private readonly Button readWhole;

    private readonly TextBlock next = Fact();

    private readonly TextBlock last = Fact();

    private readonly Link lastLink = Quiet(RoutineRowViewModel.OpenThatConversation);

    private readonly FlowPanel started = new() { Gap = Tokens.Number("DvSpace1") };

    private readonly Link startedLink = Quiet(RoutineRowViewModel.OpenStarted);

    private readonly FlowPanel actions = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly Switch runsOnItsOwn = new() { Label = RoutineRowViewModel.SwitchLabel };

    private readonly Button runOnce;

    private readonly Button change;

    private readonly Button remove;

    private readonly TextBlock onlyTheMaker = BotParts.Hint(RoutineRowViewModel.OnlyTheMaker);

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private BotScheduleResponse schedule;

    private RoutineRowViewModel? row;

    private bool showing;

    #endregion

    #region Constructors

    public RoutineRowView(RoutinesSectionViewModel part, BotScheduleResponse schedule)
    {
        (this.part, this.schedule) = (part, schedule);
        frame.Cut = Tokens.Number("DvCut");
        frame.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace4"));
        this.WhenShownOrRethemed(() => (frame.Fill, frame.Edge) = (Tokens.Brush(this, "DvBg2"), Tokens.Brush(this, "DvLine")));
        Children.Add(frame);
        sentence.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        AutomationProperties.SetHeadingLevel(sentence, AutomationHeadingLevel.Level3);
        var head = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        head.Children.Add(sentence);
        head.Children.Add(cron);
        readWhole = BotParts.Action(RoutineRowViewModel.ReadWholePrompt, () =>
        {
            row?.ToggleWholePrompt();
            Show(this.schedule);
        });
        runsOnItsOwn.OnChanged(() => showing, on => _ = part.TurnOn(this.schedule, on));
        runOnce = BotParts.Action(RoutineRowViewModel.RunOnce, () => _ = part.AskToRunOnce(this.schedule));
        change = BotParts.Action(RoutineRowViewModel.Change, () => part.Edit(this.schedule));
        remove = BotParts.Action(RoutineRowViewModel.Remove, () => part.AskToRemove(this.schedule), ButtonKind.Danger);
        actions.Margin = new(0, Tokens.Number("DvSpace1"), 0, 0);

        foreach (var control in new UIElement[] { runsOnItsOwn, runOnce, change, remove })
        {
            actions.Children.Add(control);
        }

        var startedWords = BotParts.Small(RoutineRowViewModel.StartedWords, "DvText");
        started.Children.Add(startedWords);
        started.Children.Add(startedLink);
        var lastLine = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        lastLine.Children.Add(last);
        lastLine.Children.Add(lastLink);
        var facts = BotParts.Column("DvSpace1", Line(RoutineRowViewModel.NextTerm, next), Line(RoutineRowViewModel.LastTerm, lastLine));
        frame.Children.Add(BotParts.Column("DvSpace2", head, prompt, readWhole, facts, started, actions, onlyTheMaker, problem));
    }

    #endregion

    #region Public Methods

    public void Show(BotScheduleResponse shown)
    {
        showing = true;
        schedule = shown;
        row = part.Row(shown);
        sentence.Show(row.Sentence);
        AutomationProperties.SetName(this, row.Sentence);
        cron.Shown(row.Cron is not null).Show(row.Cron);
        prompt.Show(row.Prompt);
        readWhole.Shown(row.ReadWholeShown);
        AutomationProperties.SetItemStatus(readWhole, row.WholePrompt ? "Expanded" : "Collapsed");
        AutomationProperties.SetHelpText(readWhole, row.Sentence);
        next.Show(row.Next);
        last.Show(row.Last);
        lastLink.Shown(row.Routine.LastRun?.ConversationHref is not null).Address = row.Routine.LastRun?.ConversationHref;
        started.Shown(row.StartedHref is not null);
        startedLink.Address = row.StartedHref;
        actions.Shown(row.CanChange);
        runsOnItsOwn.About = row.SwitchAbout;
        runsOnItsOwn.Show(row.Routine.Enabled);
        runsOnItsOwn.IsEnabled = runOnce.IsEnabled = change.IsEnabled = remove.IsEnabled = !row.Busy;
        change.Shown(row.CanRewrite);

        foreach (var button in new[] { runOnce, change, remove })
        {
            AutomationProperties.SetHelpText(button, row.Sentence);
        }

        onlyTheMaker.Shown(row.CanChange && !row.CanRewrite);
        problem.Show(row.Problem);
        showing = false;
        MoveFocusAfterRun(row);
    }

    #endregion

    #region Private Methods

    private static TextBlock Fact() => BotParts.Small(null, "DvText");

    // A link in the row's quieter color, as the web's routine links are.
    private static Link Quiet(string text) => new() { Text = text, Ink = "DvText2", HoverInk = "DvText", FontSize = Tokens.Number("DvText13") };

    // A term and what it says, the term at a least width so the two lines line up.
    private static Grid Line(string term, UIElement detail)
    {
        var name = BotParts.Small(term, "DvText3");
        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var line = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        name.MinWidth = Tokens.Number("DvSpace7");
        Grid.SetColumn((FrameworkElement)detail, 1);
        line.Children.Add(name);
        line.Children.Add(detail);
        return line;
    }

    // The dialog handed focus back to "Run once now", which was disabled while the run was asked for: once the answer
    // is in, focus moves to the conversation the run started, or back to the button beside the refusal.
    private void MoveFocusAfterRun(RoutineRowViewModel shown)
    {
        if (shown.TakeFocusAfterRun() is { } target)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (target is RoutineFocus.Started)
                {
                    startedLink.TakeFocus();
                }
                else
                {
                    runOnce.TakeFocus();
                }
            });
        }
    }

    #endregion
}
