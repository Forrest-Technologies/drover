namespace Drover.Ui.Pages.Bots;

/// <summary>
/// The bots of the space. Its job: pick a bot to change, or make a new one.
/// </summary>
public sealed partial class BotList(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ILogger<BotList> logger)
{
    #region Private Fields

    private const string NewBotName = "New bot";

    private IReadOnlyList<BotSummary>? bots;

    private IReadOnlyList<ModelConnectionSummary>? connections;

    private string? loadProblem;

    private string? problem;

    private bool creating;

    #endregion

    #region Properties

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    private bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loadProblem = null;

        try
        {
            connections = await api.ListModelConnections(SpaceId);
            bots = await api.ListBots(SpaceId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotList));
            loadProblem = Failures.Sentence(exception);
        }
    }

    /// <summary>Makes a bot on the connection that last answered, and opens it so that everything else is set there.</summary>
    private async Task Create()
    {
        var connection = connections!
            .OrderByDescending(candidate => candidate.Status is "connected")
            .ThenByDescending(candidate => candidate.LastCheckedAt)
            .First();
        creating = true;
        problem = null;

        try
        {
            var bot = await api.CreateBot(SpaceId, new(NewBotName, string.Empty, connection.Id, await FindModel(connection)));
            navigation.NavigateTo(Addresses.Bot(bot.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotList));
            problem = Failures.Sentence(exception);
        }
        finally
        {
            creating = false;
        }
    }

    // The connection's own default, then the model a bot already uses on it, then the first one it lists.
    private async Task<string?> FindModel(ModelConnectionSummary connection) =>
        connection.DefaultModel
        ?? bots!.FirstOrDefault(bot => bot.ModelConnectionId == connection.Id)?.Model
        ?? (await api.ListModels(SpaceId, connection.Id)).FirstOrDefault(entry => entry.State is "available")?.Model;

    #endregion
}
