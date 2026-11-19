namespace Drover.Native.Pages;

/// <summary>Find which bot spent the most, then open its most expensive runs.</summary>
public sealed class UsagePage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly UsagePageViewModel model = Make<UsagePageViewModel>();

    // The heading and the period are kept while the page shows another bot or period, so the focus stays in the
    // period control; only what follows them is drawn again.
    private readonly SegmentedControl period = new() { Label = "Period", Options = UsagePageViewModel.Periods };

    private readonly StackPanel content = new() { Spacing = Tokens.Number("DvSpace5") };

    private FocusLine? runsTitle;

    #endregion

    #region Constructors

    public UsagePage()
    {
        Content = layout;
        var header = new FlexPanel { Gap = Tokens.Number("DvSpace3"), Justify = FlexJustify.SpaceBetween };
        header.Children.Add(PageParts.Title("Usage"));
        period.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            if (period.Value is { } chosen && chosen != model.Period)
            {
                model.ChoosePeriod(chosen);
            }
        });
        header.Children.Add(period);
        layout.Body.Add(header);
        layout.Body.Add(content);
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Usage · Drover";

    /// <summary>Choosing a bot or a period changes the query only, so the page stays and its view model knows what changed.</summary>
    protected override bool StaysOpenWhenQueryChanges => true;

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        model.PeriodInAddress = route.QueryValue("period");
        model.Bot = Guid.TryParse(route.QueryValue("bot"), out var bot) ? bot : null;
        await model.Open();
        Refresh();

        if (model.TakeRunsFocus() && runsTitle is { } title)
        {
            Pieces.FocusWhenShown(title, title.TakeFocus);
        }
    }

    private void Refresh()
    {
        period.Value = model.Period;
        runsTitle = null;
        var body = content.Children;
        body.Clear();
        if (!model.IsAdmin)
        {
            body.Add(Styles.Text("Only the runs that acted for you. An owner or admin sees the whole space.", "DvTextSecondary"));
        }

        if (model.LoadProblem is { } problem)
        {
            body.Add(Failed("Drover could not add up what the bots used", problem, model.Load));
        }
        else if (model.Crew is null)
        {
            body.Add(PageParts.Waiting("Adding up what each bot used."));
        }
        else
        {
            ShowCrew();
            ShowRuns();
        }
    }

    // The crew as a table that leads with cost; a period in which nothing ran says so and shows no table.
    private void ShowCrew()
    {
        if (model.NothingRan)
        {
            content.Children.Add(Styles.Text(UsagePageViewModel.NoBotRan, "DvTextSecondary"));
            return;
        }

        content.Children.Add(new UsageTable(model.Caption, model.Rows));
    }

    private void ShowRuns()
    {
        if (model.ChosenBot is not { } bot)
        {
            return;
        }

        // The heading the focus moves to when a bot is chosen. It is not a stop on the way through the page.
        var title = new FocusLine();
        title.Words.Style = (Style)Styles.Dictionary["DvTextH2"];
        title.Say(Drover.Ui.Usage.UsageWords.RunsTitle(bot.BotName, model.Period));
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        runsTitle = title;
        content.Children.Add(title);
        if (model.RunsProblem is { } problem)
        {
            content.Children.Add(Failed("Drover could not list the runs", problem, model.LoadRuns));
        }
        else if (model.Runs is not { } runs)
        {
            content.Children.Add(PageParts.Waiting("Finding the runs that cost the most."));
        }
        else
        {
            if (runs.Runs.Count == 0)
            {
                content.Children.Add(Styles.Text(Drover.Ui.Usage.UsageWords.NoRuns(bot.BotName, model.Period), "DvTextSecondary"));
            }

            foreach (var run in runs.Runs)
            {
                var row = PageParts.Column(
                    Styles.Text(Drover.Ui.Usage.UsageWords.Cost(run.Usage, runs.Currency)),
                    new Link { Text = Drover.Ui.Usage.UsageWords.RunPlace(run), Address = UsagePageViewModel.ConversationOf(run) },
                    Styles.Text($"{Drover.Ui.Usage.UsageWords.Tokens(run.Usage.InputTokens)} tokens in, {Drover.Ui.Usage.UsageWords.Tokens(run.Usage.OutputTokens)} out", "DvTextMono"));
                if (Drover.Ui.Usage.UsageWords.LeftOut(run.Usage) is { } leftOut)
                {
                    row.Children.Add(Styles.Text(leftOut, "DvTextSecondary"));
                }

                content.Children.Add(row);
            }

            if (runs.Unopened is { } unopened)
            {
                content.Children.Add(Styles.Text(Drover.Ui.Usage.UsageWords.Unopened(unopened, runs.Currency, afterListed: runs.Runs.Count > 0), "DvTextSecondary"));
            }
        }
    }

    private EmptyState Failed(string title, string cause, Func<Task> retry)
    {
        var failed = new EmptyState { Title = title, Cause = cause, ActionLabel = "Try again" };
        failed.Action += async (_, _) => { var pending = retry(); Refresh(); await pending; Refresh(); };
        return failed;
    }

    #endregion
}
