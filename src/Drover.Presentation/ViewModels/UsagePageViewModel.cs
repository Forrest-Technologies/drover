namespace Drover.Presentation.ViewModels;

/// <summary>
/// The usage page. Its job: find which bot is spending the most, to decide what to change. The Crew is a table that
/// leads with cost; choosing a bot lists its most expensive Runs, each linking to its conversation. The period and
/// the chosen bot live in the address, so a link to the page shows the same thing.
/// </summary>
public sealed partial class UsagePageViewModel(DroverApi api, SessionState session, Navigator navigation, ILogger<UsagePageViewModel> logger) : ObservableObject
{
    #region Private Fields

    public static readonly IReadOnlyList<SegmentOption<string>> Periods =
        [.. UsagePeriods.All.Select(period => new SegmentOption<string>(period, UsageWords.PeriodName(period)))];

    [ObservableProperty]
    private CrewUsage? crew;

    [ObservableProperty]
    private BotRunsUsage? runs;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? runsProblem;

    private string? loadedPeriod;

    private Guid? loadedBot;

    [ObservableProperty]
    private bool focusRuns;


    #endregion

    #region Properties

    /// <summary>The period in the address: today, 7d, or 30d. Anything else is the last 7 days.</summary>
    public string? PeriodInAddress { get; set; }

    /// <summary>The bot whose most expensive Runs are listed, when one was chosen.</summary>
    public Guid? Bot { get; set; }

    public string Period => PeriodInAddress is { } period && UsagePeriods.All.Contains(period) ? period : UsagePeriods.Week;

    // The server decides what each person reads. Here it only says which of the two the person is reading.
    public bool IsAdmin => session.Space?.Role is "owner" or "admin";

    public UsageNode? ChosenBot => Bot is { } id && Crew is not null && !UsageWords.IsEmpty(Crew.Crew) ? Find(Crew.Crew, id) : null;

    /// <summary>The table's columns, cost first after the bot: the figure that decides what to change.</summary>
    public static IReadOnlyList<string> Columns => ["Bot", "Cost", "Tokens in", "Tokens out", "Model"];

    public static string NoBotRan => "No bot ran in this period.";

    /// <summary>What the table shows and for when, such as "What each bot used in the last 7 days".</summary>
    public string Caption => UsageWords.Caption(Period);

    /// <summary>True when the crew is loaded and nothing ran in the period, so the screen says so and shows no table.</summary>
    public bool NothingRan => Crew is { } loaded && UsageWords.IsEmpty(loaded.Crew);

    /// <summary>
    /// The crew as the table's rows: the hierarchy it is, the biggest spender first at every level, each name linking
    /// to that bot's most expensive runs in the period, the chosen one marked.
    /// </summary>
    public IReadOnlyList<UsageTableRow> Rows => Crew is not { } loaded || NothingRan ? [] :
    [
        .. UsageWords.Rows(loaded.Crew).Select(row =>
            UsageTableRow.Of(row, loaded.Currency, Addresses.UsageFor(Period, row.Bot.BotId), row.Bot.BotId == ChosenBot?.BotId)),
    ];

    private static string Zone => TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : "UTC";

    #endregion

    #region Overrides

    public async Task Open()
    {
        if (await session.Load() is null)
        {
            return;
        }

        if (Period != loadedPeriod)
        {
            await Load();
        }
        else if (Bot != loadedBot)
        {
            // Only a bot the person just chose moves focus to its Runs. A period change keeps focus in the control.
            FocusRuns = true;
            await LoadRuns();
        }
    }

    #endregion

    #region Private Methods

    public async Task Load()
    {
        loadedPeriod = Period;
        LoadProblem = null;
        FocusRuns = false;
        Crew = null;

        try
        {
            Crew = await api.GetCrewUsage(session.Space!.Id, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsagePageViewModel));
            LoadProblem = Failures.Sentence(exception);
            return;
        }

        await LoadRuns();
    }

    public async Task LoadRuns()
    {
        loadedBot = Bot;
        Runs = null;
        RunsProblem = null;

        if (ChosenBot is not { } bot)
        {
            return;
        }

        try
        {
            Runs = await api.ListCostliestRuns(session.Space!.Id, bot.BotId, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsagePageViewModel));
            RunsProblem = Failures.Sentence(exception);
            FocusRuns = false;
        }
    }

    public void ChoosePeriod(string period) => navigation.NavigateTo(Addresses.UsageFor(period, Bot));

    public static string ConversationOf(RunCost run) =>
        run.IsMain ? Addresses.Talk(run.ThreadBotId) : Addresses.SideChat(run.ThreadBotId, run.ThreadId);

    private static UsageNode? Find(IEnumerable<UsageNode> level, Guid botId)
    {
        foreach (var node in level)
        {
            if (node.BotId == botId)
            {
                return node;
            }

            if (Find(node.Children, botId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    #endregion
}
