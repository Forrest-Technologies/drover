namespace Drover.Presentation.ViewModels;

/// <summary>
/// The status page. Its job: see whether this server and the space's connections are working, and what to do
/// about the ones that are not.
/// </summary>
public sealed partial class StatusPageViewModel(DroverApi api, SessionState session, ILogger<StatusPageViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    private StatusReport? report;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private bool loading;

    #endregion

    #region Properties

    // The server decides this for every request. Here it only keeps a request a person cannot make off the screen.
    public bool CanSee => session.Space?.Role is "owner" or "admin";

    public List<StatusRow> ServerRows => [.. Report?.Rows.Where(row => row.Area == "server") ?? []];

    public List<StatusRow> SpaceRows => [.. Report?.Rows.Where(row => row.Area == "space") ?? []];

    #endregion

    #region Overrides

    public async Task Open()
    {
        if (await session.Load() is not null && CanSee)
        {
            await Load();
        }
    }

    #endregion

    #region Private Methods

    public async Task Load()
    {
        Loading = true;
        LoadProblem = null;

        try
        {
            Report = await api.GetStatus(session.Space!.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(StatusPageViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
        finally
        {
            Loading = false;
        }
    }

    #endregion
}
