namespace Drover.Native.Pages;

/// <summary>Find which bot spent the most, then open its most expensive runs.</summary>
public sealed class UsagePage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly UsagePageViewModel model = Make<UsagePageViewModel>();

    #endregion

    #region Constructors

    public UsagePage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Usage · Drover";

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        model.PeriodInAddress = route.QueryValue("period");
        model.Bot = Guid.TryParse(route.QueryValue("bot"), out var bot) ? bot : null;
        await model.Open();
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        var header = new FlexPanel { Gap = Tokens.Number("DvSpace3"), Justify = FlexJustify.SpaceBetween };
        header.Children.Add(PageParts.Title("Usage"));
        var period = new SegmentedControl { Label = "Period", Options = UsagePageViewModel.Periods, Value = model.Period };
        period.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            if (period.Value is { } chosen && chosen != model.Period)
            {
                model.ChoosePeriod(chosen);
            }
        });
        header.Children.Add(period);
        body.Add(header);
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
            layout.Body.Add(Styles.Text(UsagePageViewModel.NoBotRan, "DvTextSecondary"));
            return;
        }

        layout.Body.Add(new UsageTable(model.Caption, model.Rows));
    }

    private void ShowRuns()
    {
        if (model.ChosenBot is not { } bot)
        {
            return;
        }

        var title = Styles.Text(Drover.Ui.Usage.UsageWords.RunsTitle(bot.BotName, model.Period), "DvTextH2");
        layout.Body.Add(title);
        if (model.RunsProblem is { } problem)
        {
            layout.Body.Add(Failed("Drover could not list the runs", problem, model.LoadRuns));
        }
        else if (model.Runs is not { } runs)
        {
            layout.Body.Add(PageParts.Waiting("Finding the runs that cost the most."));
        }
        else
        {
            if (runs.Runs.Count == 0)
            {
                layout.Body.Add(Styles.Text(Drover.Ui.Usage.UsageWords.NoRuns(bot.BotName, model.Period), "DvTextSecondary"));
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

                layout.Body.Add(row);
            }

            if (runs.Unopened is { } unopened)
            {
                layout.Body.Add(Styles.Text(Drover.Ui.Usage.UsageWords.Unopened(unopened, runs.Currency, afterListed: runs.Runs.Count > 0), "DvTextSecondary"));
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
