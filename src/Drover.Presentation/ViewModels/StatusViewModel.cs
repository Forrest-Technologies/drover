namespace Drover.Presentation.ViewModels;

/// <summary>
/// The status page. Its job: see whether this server and the space's connections are working, and what to do
/// about the ones that are not.
/// </summary>
public sealed partial class StatusViewModel(DroverApi api, SessionState session, ILogger<StatusViewModel> logger) : ObservableObject
{
    #region Private Fields

    private static readonly string[] stateNames =
    [
        nameof(ShowCheckAgain), nameof(ShowRestricted), nameof(ShowFailed), nameof(ShowChecking), nameof(ShowReport), nameof(ShowServer),
        nameof(ServerRows), nameof(SpaceRows), nameof(ShowNoSpaceRows), nameof(ShowSpaceRows),
    ];

    private StatusReport? report;

    private bool asked;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckAgain))]
    private bool loading;

    #endregion

    #region Properties

    public static string Title => "Status · Drover";

    public static string Heading => "Status";

    public static string RestrictedNote => "An owner or admin of the space can see its status.";

    public static string FailedTitle => "Drover could not load the status";

    public static string CheckingNote => "Checking the server and the space's connections.";

    public static string ServerHeading => "This server";

    public static string SpaceHeading => "This space";

    public static string NoSpaceRowsBefore => "No model or tool connection yet.";

    public static string AddModelLabel => "Add a model connection";

    public static string AddToolLabel => "a tool server";

    public static string NoSpaceRowsAfter => "and it is listed here.";

    // The server decides this for every request. Here it only keeps a request a person cannot make off the screen.
    public bool CanSee => session.Space?.Role is "owner" or "admin";

    public bool ShowCheckAgain => report is not null;

    public bool CanCheckAgain => !Loading;

    public bool ShowRestricted => asked && !CanSee;

    public bool ShowFailed => CanSee && LoadProblem is not null;

    public bool ShowChecking => !ShowRestricted && !ShowFailed && report is null;

    public bool ShowReport => CanSee && LoadProblem is null && report is not null;

    public bool ShowServer => report is { ServerRowsShown: true };

    public IReadOnlyList<StatusEntry> ServerRows => [.. report?.Rows.Where(row => row.Area == "server").Select(StatusEntry.From) ?? []];

    public IReadOnlyList<StatusEntry> SpaceRows => [.. report?.Rows.Where(row => row.Area == "space").Select(StatusEntry.From) ?? []];

    public bool ShowNoSpaceRows => SpaceRows.Count == 0;

    public bool ShowSpaceRows => SpaceRows.Count > 0;

    #endregion

    #region Public Methods

    public async Task Start()
    {
        if (await session.Load() is not null)
        {
            asked = true;
            Show();

            if (CanSee)
            {
                await Load();
            }
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        Loading = true;
        LoadProblem = null;

        try
        {
            report = await api.GetStatus(session.Space!.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(StatusViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
        finally
        {
            Loading = false;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        foreach (var stateName in stateNames)
        {
            OnPropertyChanged(stateName);
        }
    }

    #endregion
}
