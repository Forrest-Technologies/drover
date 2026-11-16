namespace Drover.Ui.Pages.Usage;

/// <summary>
/// The usage page. Its job: find which bot is spending the most, to decide what to change. The crew is a table that
/// leads with cost; choosing a bot lists its most expensive runs, each linking to its conversation. The period and
/// the chosen bot live in the address, so a link to the page shows the same thing.
/// </summary>
public sealed partial class UsagePage(DroverApi api, SessionState session, NavigationManager navigation, ILogger<UsagePage> logger)
{
    #region Private Fields

    private static readonly IReadOnlyList<SegmentOption<string>> periods =
        [.. UsagePeriods.All.Select(period => new SegmentOption<string>(period, UsageWords.PeriodName(period)))];

    private CrewUsage? crew;

    private BotRunsUsage? runs;

    private string? loadProblem;

    private string? runsProblem;

    private string? loadedPeriod;

    private Guid? loadedBot;

    private bool focusRuns;

    private ElementReference runsHeading;

    #endregion

    #region Properties

    /// <summary>The period in the address: today, 7d, or 30d. Anything else is the last 7 days.</summary>
    [SupplyParameterFromQuery(Name = "period")]
    public string? PeriodInAddress { get; set; }

    /// <summary>The bot whose most expensive runs are listed, when one was chosen.</summary>
    [SupplyParameterFromQuery(Name = "bot")]
    public Guid? Bot { get; set; }

    private string Period => PeriodInAddress is { } period && UsagePeriods.All.Contains(period) ? period : UsagePeriods.Week;

    // The server decides what each person reads. Here it only says which of the two the person is reading.
    private bool IsAdmin => session.Space?.Role is "owner" or "admin";

    private UsageNode? ChosenBot => Bot is { } id && crew is not null && !UsageWords.IsEmpty(crew.Crew) ? Find(crew.Crew, id) : null;

    private static string Zone => TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : "UTC";

    #endregion

    #region Overrides

    protected override async Task OnParametersSetAsync()
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
            // Only a bot the person just chose moves focus to its runs. A period change keeps focus in the control.
            focusRuns = true;
            await LoadRuns();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusRuns && runs is not null)
        {
            focusRuns = false;
            await runsHeading.FocusAsync();
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loadedPeriod = Period;
        loadProblem = null;
        focusRuns = false;
        crew = null;

        try
        {
            crew = await api.GetCrewUsage(session.Space!.Id, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsagePage));
            loadProblem = Failures.Sentence(exception);
            return;
        }

        await LoadRuns();
    }

    private async Task LoadRuns()
    {
        loadedBot = Bot;
        runs = null;
        runsProblem = null;

        if (ChosenBot is not { } bot)
        {
            return;
        }

        try
        {
            runs = await api.ListCostliestRuns(session.Space!.Id, bot.BotId, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsagePage));
            runsProblem = Failures.Sentence(exception);
            focusRuns = false;
        }
    }

    private void ChoosePeriod(string period) => navigation.NavigateTo(Addresses.UsageFor(period, Bot));

    private static string ConversationOf(RunCost run) =>
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
