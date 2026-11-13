namespace Drover.Ui.Pages.Status;

/// <summary>
/// The status page. Its job: see whether this server and the space's connections are working, and what to do
/// about the ones that are not.
/// </summary>
public sealed partial class StatusPage(DroverApi api, SessionState session, ILogger<StatusPage> logger)
{
    #region Private Fields

    private StatusReport? report;

    private string? loadProblem;

    private bool loading;

    #endregion

    #region Properties

    // The server decides this for every request. Here it only keeps a request a person cannot make off the screen.
    private bool CanSee => session.Space?.Role is "owner" or "admin";

    private List<StatusRow> ServerRows => [.. report?.Rows.Where(row => row.Area == "server") ?? []];

    private List<StatusRow> SpaceRows => [.. report?.Rows.Where(row => row.Area == "space") ?? []];

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        if (await session.Load() is not null && CanSee)
        {
            await Load();
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loading = true;
        loadProblem = null;

        try
        {
            report = await api.GetStatus(session.Space!.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(StatusPage));
            loadProblem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
        }
    }

    #endregion
}
