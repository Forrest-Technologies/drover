namespace Drover.Presentation.ViewModels;

/// <summary>Where the focus goes on the screen a choice opens: the control that was used, or the runs a bot's name opened.</summary>
public enum UsageFocus
{
    None,
    Period,
    Runs,
}

/// <summary>One run of a bot's most expensive runs: its cost, the link to its conversation, its tokens.</summary>
public sealed record UsageRunLine(Guid RunId, string Cost, string Address, string Place, string Tokens, string? LeftOut);

/// <summary>
/// The usage page. Its job: find which bot is spending the most, to decide what to change. The crew is a table that
/// leads with cost; choosing a bot lists its most expensive runs, each linking to its conversation. The period and
/// the chosen bot live in the address, so a link to the page shows the same thing.
/// </summary>
public sealed partial class UsageViewModel(DroverApi api, SessionState session, Navigator navigator, ILogger<UsageViewModel> logger)
    : ObservableObject
{
    #region Private Fields

    private static readonly IReadOnlyList<SegmentOption<string>> periods =
        [.. UsagePeriods.All.Select(period => new SegmentOption<string>(period, UsageWords.PeriodName(period)))];

    private static readonly string[] stateNames =
    [
        nameof(ShowOwnRunsHint), nameof(ShowFailed), nameof(ShowLoading), nameof(ShowCrew), nameof(Table), nameof(ShowRuns),
        nameof(RunsTitle), nameof(ShowRunsFailed), nameof(ShowRunsLoading), nameof(ShowRunsList), nameof(ShowNoRuns), nameof(NoRunsNote),
        nameof(RunLines), nameof(ShowUnopened), nameof(UnopenedNote), nameof(Runs),
    ];

    private CrewUsage? crew;

    private string? loadedPeriod;

    private Guid? loadedBot;

    private Guid? bot;

    private bool opening;

    [ObservableProperty]
    private BotRunsUsage? runs;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? runsProblem;

    [ObservableProperty]
    private string period = UsagePeriods.Week;

    #endregion

    #region Properties

    public static string Title => "Usage · Drover";

    public static string Heading => "Usage";

    public static string PeriodLabel => "Period";

    public static string OwnRunsHint => "Only the runs that acted for you. An owner or admin sees the whole space.";

    public static string FailedTitle => "Drover could not add up what the bots used";

    public static string LoadingNote => "Adding up what each bot used.";

    public static string RunsFailedTitle => "Drover could not list the runs";

    public static string RunsLoadingNote => "Finding the runs that cost the most.";

    public static IReadOnlyList<SegmentOption<string>> Periods => periods;

    /// <summary>True when the last load of the runs was for a bot the person just chose, so focus moves to its heading.</summary>
    public bool FocusRuns { get; private set; }

    // The server decides what each person reads. Here it only says which of the two the person is reading.
    public bool IsAdmin => session.Space?.Role is "owner" or "admin";

    public bool ShowOwnRunsHint => !IsAdmin;

    public bool ShowFailed => LoadProblem is not null;

    public bool ShowLoading => LoadProblem is null && crew is null;

    public bool ShowCrew => LoadProblem is null && crew is not null;

    public UsageTableModel? Table => crew is null
        ? null
        : new(crew.Crew, crew.Currency, UsageWords.Caption(Period), id => Addresses.UsageFor(Period, id), ChosenBot?.BotId);

    public UsageNode? ChosenBot => bot is { } id && crew is not null && !UsageWords.IsEmpty(crew.Crew) ? Find(crew.Crew, id) : null;

    public bool ShowRuns => ShowCrew && ChosenBot is not null;

    public string RunsTitle => ChosenBot is { } chosen ? UsageWords.RunsTitle(chosen.BotName, Period) : string.Empty;

    public bool ShowRunsFailed => ShowRuns && RunsProblem is not null;

    public bool ShowRunsLoading => ShowRuns && RunsProblem is null && Runs is null;

    public bool ShowRunsList => ShowRuns && RunsProblem is null && Runs is { Runs.Count: > 0 };

    public bool ShowNoRuns => ShowRuns && RunsProblem is null && Runs is { Runs.Count: 0 };

    public string NoRunsNote => ChosenBot is { } chosen ? UsageWords.NoRuns(chosen.BotName, Period) : string.Empty;

    public IReadOnlyList<UsageRunLine> RunLines => Runs is { } listed
        ? [.. listed.Runs.Select(run => new UsageRunLine(
            run.RunId,
            UsageWords.Cost(run.Usage, listed.Currency),
            ConversationOf(run),
            UsageWords.RunPlace(run),
            $"{UsageWords.Tokens(run.Usage.InputTokens)} tokens in, {UsageWords.Tokens(run.Usage.OutputTokens)} out",
            UsageWords.LeftOut(run.Usage)))]
        : [];

    public bool ShowUnopened => ShowRuns && RunsProblem is null && Runs is { Unopened: not null };

    public string UnopenedNote => Runs is { Unopened: { } unopened } listed
        ? UsageWords.Unopened(unopened, listed.Currency, afterListed: listed.Runs.Count > 0)
        : string.Empty;

    private static string Zone => TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : "UTC";


    /// <summary>
    /// Raised just before a choice takes the person to another address of this screen, and says where focus should
    /// go on the screen that opens, so a choice made with the keyboard does not lose its place.
    /// </summary>
    public event Action<UsageFocus>? Navigating;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows what the address names. <paramref name="periodInAddress"/> is today, 7d, or 30d, and anything else is the
    /// last 7 days; <paramref name="botInAddress"/> is the bot whose most expensive runs are listed, when one was chosen.
    /// </summary>
    public async Task Open(string? periodInAddress, Guid? botInAddress)
    {
        opening = true;
        Period = periodInAddress is { } chosen && UsagePeriods.All.Contains(chosen) ? chosen : UsagePeriods.Week;
        opening = false;
        bot = botInAddress;

        if (await session.Load() is null)
        {
            return;
        }

        if (Period != loadedPeriod)
        {
            await Load();
        }
        else if (bot != loadedBot)
        {
            // Only a bot the person just chose moves focus to its runs. A period change keeps focus in the control.
            FocusRuns = true;
            await LoadRuns();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        loadedPeriod = Period;
        LoadProblem = null;
        FocusRuns = false;
        crew = null;
        Show();

        try
        {
            crew = await api.GetCrewUsage(session.Space!.Id, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsageViewModel));
            LoadProblem = Failures.Sentence(exception);
            Show();
            return;
        }

        await LoadRuns();
    }

    [RelayCommand]
    public async Task LoadRuns()
    {
        loadedBot = bot;
        Runs = null;
        RunsProblem = null;
        Show();

        if (ChosenBot is not { } chosen)
        {
            return;
        }

        try
        {
            Runs = await api.ListCostliestRuns(session.Space!.Id, chosen.BotId, Period, Zone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(UsageViewModel));
            RunsProblem = Failures.Sentence(exception);
            FocusRuns = false;
        }

        Show();
    }

    /// <summary>The person chose a bot in the table: its most expensive runs are listed, and focus moves to them.</summary>
    public void ChooseBot(Guid botId)
    {
        Navigating?.Invoke(UsageFocus.Runs);
        navigator.NavigateTo(Addresses.UsageFor(Period, botId));
    }

    public void ChoosePeriod(string chosen)
    {
        Navigating?.Invoke(UsageFocus.Period);
        navigator.NavigateTo(Addresses.UsageFor(chosen, bot));
    }

    #endregion

    #region Private Methods

    // The control is bound both ways: a period chosen in it takes the person to the address that names it.
    partial void OnPeriodChanged(string value)
    {
        if (!opening)
        {
            ChoosePeriod(value);
        }
    }

    private void Show()
    {
        foreach (var stateName in stateNames)
        {
            OnPropertyChanged(stateName);
        }
    }

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
