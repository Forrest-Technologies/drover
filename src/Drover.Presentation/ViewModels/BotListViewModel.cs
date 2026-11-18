namespace Drover.Presentation.ViewModels;

/// <summary>
/// The Bots of the space. Its job: pick a bot to change, or make a new one.
/// </summary>
public sealed partial class BotListViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigation,
    ILogger<BotListViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string NewBotName = "New bot";

    [ObservableProperty]
    private IReadOnlyList<BotSummary>? bots;

    [ObservableProperty]
    private IReadOnlyList<ModelConnectionSummary>? connections;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool creating;

    #endregion

    #region Properties

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Overrides

    public async Task Open()
    {
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    #endregion

    #region Private Methods

    public async Task Load()
    {
        LoadProblem = null;

        try
        {
            Connections = await api.ListModelConnections(SpaceId);
            Bots = await api.ListBots(SpaceId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotListViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    /// <summary>
    /// Makes a bot on the connection that last answered, at the top of the crew or under <paramref name="parentBotId"/>,
    /// and opens it so that everything else is set there.
    /// </summary>
    public async Task Create(Guid? parentBotId)
    {
        var connection = Connections!
            .OrderByDescending(candidate => candidate.Status is "connected")
            .ThenByDescending(candidate => candidate.LastCheckedAt)
            .First();
        Creating = true;
        Problem = null;

        try
        {
            var bot = await api.CreateBot(SpaceId, new(NewBotName, string.Empty, connection.Id, await FindModel(connection), parentBotId));
            navigation.NavigateTo(Addresses.Bot(bot.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotListViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Creating = false;
        }
    }

    // A bot that another bot made says which one, when that bot is still here.
    public string MadeBy(BotSummary bot) =>
        bot.MadeByBotId is { } maker && Bots!.FirstOrDefault(candidate => candidate.Id == maker) is { } made
            ? $" · made by {made.Name}"
            : string.Empty;

    // The connection's own default, then the model a bot already uses on it, then the first one it lists.
    private async Task<string?> FindModel(ModelConnectionSummary connection) =>
        connection.DefaultModel
        ?? Bots!.FirstOrDefault(bot => bot.ModelConnectionId == connection.Id)?.Model
        ?? (await api.ListModels(SpaceId, connection.Id)).FirstOrDefault(entry => entry.State is "available")?.Model;

    #endregion
}
