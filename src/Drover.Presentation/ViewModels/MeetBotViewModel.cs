namespace Drover.Presentation.ViewModels;

/// <summary>
/// First-run setup, step three: the first bot, which leads the crew. A space that already has a bot meets that bot
/// and makes no other.
/// </summary>
public sealed partial class MeetBotViewModel(
    DroverApi api, SessionState session, ILogger<MeetBotViewModel> logger) : ObservableObject
{
    #region Private Fields

    // A role, said plainly: the one who marshals the crew. The person can type any other name.
    private const string StarterName = "Marshal";

    private const string DefaultTrait = "default";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModelList), nameof(ShowModelName), nameof(ModelChoices))]
    private IReadOnlyList<ModelCatalogResponse> models = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoModel), nameof(ShowForm))]
    private ModelConnectionSummary? connection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowMet), nameof(ShowNoModel), nameof(ShowForm), nameof(BotName), nameof(BotModel), nameof(BotHue), nameof(TalkAddress),
        nameof(MetLead), nameof(TalkLabel))]
    private BotSummary? bot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownName), nameof(CreateLabel))]
    private string name = StarterName;

    [ObservableProperty]
    private string chosenModel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFailed), nameof(ShowLoading), nameof(ShowMet), nameof(ShowNoModel), nameof(ShowForm))]
    private string? loadProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelNameHint))]
    private string? listProblem;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading), nameof(ShowMet), nameof(ShowNoModel), nameof(ShowForm))]
    private bool loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateLabel))]
    private bool busy;

    #endregion

    #region Properties

    public static string Title => "Meet your first bot";

    public static string LoadingNote => "Asking the provider which models it has.";

    public static string NoModelTitle => "No model is connected";

    public static string NoModelCause => "A bot needs a model to think with, and this space has none yet.";

    public static string NoModelAction => "Connect a model";

    public static string FormLead =>
        "Your first bot leads the crew. You talk to it, and the bots it makes for you work beneath it. Name it and choose the model it thinks with.";

    public bool ShowFailed => LoadProblem is not null;

    public bool ShowLoading => LoadProblem is null && !Loaded;

    public bool ShowMet => LoadProblem is null && Loaded && Bot is not null;

    public bool ShowNoModel => LoadProblem is null && Loaded && Bot is null && Connection is null;

    public bool ShowForm => LoadProblem is null && Loaded && Bot is null && Connection is not null;

    public bool ShowModelList => Models.Count > 0;

    public bool ShowModelName => Models.Count == 0;

    /// <summary>The models the provider offers, as the choices of the list.</summary>
    public IReadOnlyList<Choice> ModelChoices => [.. Models.Select(entry => new Choice(entry.Model, entry.DisplayName))];

    public string BotName => Bot?.Name ?? string.Empty;

    public string BotModel => Bot?.Model ?? string.Empty;

    public int BotHue => Bot is { } met ? BotIdentity.Hue(met.Id) : 0;

    public string MetLead => $"{BotName} is ready. It leads your crew: the bots it makes for you, and the ones you add, work beneath it.";

    public string TalkLabel => $"Talk to {BotName}";

    public string TalkAddress => Bot is { } met ? Addresses.Talk(met.Id) : Addresses.Home;

    public string ModelNameHint => ListProblem ?? "This endpoint does not list its models. Type the name it expects.";

    public string ShownName => Name.Trim().Length == 0 ? "the bot" : Name.Trim();

    public string CreateLabel => Busy ? $"Creating {ShownName}" : $"Create {ShownName}";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    public async Task Start()
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

    [RelayCommand]
    public async Task Create()
    {
        Busy = true;
        Problem = null;
        try
        {
            Bot = await api.CreateBot(SpaceId, new(Name.Trim(), LeadInstructions(Name.Trim()), Connection!.Id, ChosenModel.Trim()));
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

    #region Private Methods

    // The connection that last answered a test comes first: it is the one the person just set up.
    private static ModelConnectionSummary? PickConnection(IReadOnlyList<ModelConnectionSummary> connections) =>
        connections.OrderByDescending(candidate => candidate.Status is "connected").ThenByDescending(candidate => candidate.LastCheckedAt).FirstOrDefault();

    private async Task ListModels(ModelConnectionSummary chosen)
    {
        var refresh = await api.RefreshModels(SpaceId, chosen.Id);
        Models = [.. refresh.Models.Where(entry => entry.State is "available").OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)];
        ListProblem = refresh.Succeeded ? null : refresh.Message;
        ChosenModel = Models.FirstOrDefault(entry => entry.Traits.Contains(DefaultTrait))?.Model
            ?? chosen.DefaultModel
            ?? (Models.Count > 0 ? Models[0].Model : null)
            ?? string.Empty;
    }

    // What the lead is told. It holds on a server that lets bots build bots and on one that does not: with its crew
    // tools the lead makes the bot, and the person approves each use; without them it says which bot the job needs.
    private static string LeadInstructions(string leadName) =>
        string.Concat(
            $"You are {leadName}, the lead of this person's crew of bots: their chief of staff. Take what they ask and do what you can yourself. ",
            "When a job would be better done by a bot of its own, or the person asks for one, and you have crew tools, make it beneath you: ",
            "give it a name, say in two sentences what it is for, and write its instructions. The person is asked to approve each bot before it exists. ",
            "A new bot starts with few tools, so say which tools it still needs and the person can allow them. ",
            "If you have no crew tools, name the bot, say what it is for and which tools it needs, and the person can add it beneath you. ",
            "Answer plainly and keep it short unless asked for more.");

    #endregion
}
