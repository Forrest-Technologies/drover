namespace Drover.Presentation.ViewModels;

/// <summary>The MeetBot setup step, with the same server calls and transitions as the existing client.</summary>
public sealed partial class MeetBotViewModel(
    DroverApi api,
    SessionState session,
    ILogger<MeetBotViewModel> logger) : ObservableObject
{
    #region Private Fields

    // A role, said plainly: the one who marshals the crew. The person can type any other Name.
    private const string StarterName = "Marshal";

    private const string DefaultTrait = "default";

    [ObservableProperty]
    private IReadOnlyList<ModelCatalogResponse> models = [];

    [ObservableProperty]
    private ModelConnectionSummary? connection;

    [ObservableProperty]
    private BotSummary? bot;

    [ObservableProperty]
    private string name = StarterName;

    [ObservableProperty]
    private string model = string.Empty;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? listProblem;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool loaded;

    [ObservableProperty]
    private bool busy;

    #endregion

    #region Properties

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    public string ShownName => Name.Trim().Length == 0 ? "the bot" : Name.Trim();

    #endregion

    #region Private Methods

    public async Task Open()
    {
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        LoadProblem = null;
        try
        {
            Bot = (await api.ListBots(SpaceId)).FirstOrDefault();
            Connection = Bot is null ? PickConnection(await api.ListModelConnections(SpaceId)) : null;
            if (Connection is not null)
            {
                await ListModels(Connection);
            }

            Loaded = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(MeetBotViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    // The Connection that last answered a test comes first: it is the one the person just set up.
    private static ModelConnectionSummary? PickConnection(IReadOnlyList<ModelConnectionSummary> connections) =>
        connections.OrderByDescending(candidate => candidate.Status is "connected").ThenByDescending(candidate => candidate.LastCheckedAt).FirstOrDefault();

    private async Task ListModels(ModelConnectionSummary chosen)
    {
        var refresh = await api.RefreshModels(SpaceId, chosen.Id);
        Models = [.. refresh.Models.Where(entry => entry.State is "available").OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)];
        ListProblem = refresh.Succeeded ? null : refresh.Message;
        Model = Models.FirstOrDefault(entry => entry.Traits.Contains(DefaultTrait))?.Model
            ?? chosen.DefaultModel
            ?? (Models.Count > 0 ? Models[0].Model : null)
            ?? string.Empty;
    }

    [RelayCommand]
    public async Task Create()
    {
        Busy = true;
        Problem = null;
        try
        {
            Bot = await api.CreateBot(SpaceId, new(Name.Trim(), FirstBot.Instructions(Name.Trim()), Connection!.Id, Model.Trim()));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(MeetBotViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
