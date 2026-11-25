namespace Drover.Presentation.ViewModels;

/// <summary>
/// The bots of the space. Its job: pick a bot to change, or make a new one.
/// </summary>
public sealed partial class BotListViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<BotListViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(Crew), nameof(ShowsNewBot), nameof(ShowsAddUnder))]
    private IReadOnlyList<BotSummary>? bots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(ShowsNewBot), nameof(ShowsAddUnder))]
    private IReadOnlyList<ModelConnectionSummary>? connections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private string? loadProblem;

    #endregion

    #region Properties

    public static string Title => "Bots · Drover";

    public static string Heading => "Bots";

    public static string LoadFailedTitle => "Drover could not load the bots";

    public static string TryAgain => "Try again";

    public static string Loading => "Loading the bots.";

    public static string NoModelTitle => "No model is connected";

    public static string NoModelCause => "A bot needs a model to think with, and this space has none yet.";

    public static string NoModelAction => "Connect a model";

    public static string NoModelAddress => Addresses.SetupModel;

    public static string NoBotTitle => "No bot yet";

    public static string NoBotCause => "Make one, then give it a name, a purpose, and its tools.";

    public static string NoBotForMember => "This space has no bots yet. An owner or admin of the space can create one.";

    public static string MemberHint => "Owners and admins of this space change its bots. You can see them and talk to them.";

    public static string ListName => "Bots";

    public static string PausedBadge => "Paused";

    /// <summary>Which of the screen's states it is in, in the order the web screen decides them.</summary>
    public BotListState State => (LoadProblem, Bots, Connections) switch
    {
        ({ }, _, _) => BotListState.Failed,
        (_, null, _) or (_, _, null) => BotListState.Loading,
        (_, _, { Count: 0 }) => BotListState.NoModel,
        (_, { Count: 0 }, _) when CanEdit => BotListState.NoBot,
        (_, { Count: 0 }, _) => BotListState.NoBotForMember,
        _ => BotListState.Listed,
    };

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>The header's button, shown when there is a bot already and a model to make another with.</summary>
    public bool ShowsNewBot => CanEdit && Bots is { Count: > 0 } && Connections is { Count: > 0 };

    /// <summary>Each row's button that makes a bot under it.</summary>
    public bool ShowsAddUnder => CanEdit && Connections is { Count: > 0 };

    public static string NewBotLabel => "New bot";

    public static string MakeBotLabel => "Make a bot";

    /// <summary>The bots as the crew they form: a bot, then the bots it manages, set in beneath it.</summary>
    public IReadOnlyList<CrewNode> Crew => Bots is null ? [] : CrewTree.From(Bots);

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>Runs when the screen opens: the bots are read once it is known who is signed in.</summary>
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
    /// Opens the editor on a bot that is not made yet, at the top of the crew or under <paramref name="parentBotId"/>.
    /// Nothing is stored until the person saves it there.
    /// </summary>
    [RelayCommand]
    public void Create(Guid? parentBotId) => navigator.NavigateTo(Addresses.NewBot(parentBotId));

    /// <summary>The line under a bot's name: its handle, its model, and the bot that made it, when that bot is still here.</summary>
    public string Facts(BotSummary bot) => $"@{bot.Handle} · {bot.Model}{MadeBy(bot)}";

    public static string AddUnderLabel(BotSummary bot) => $"New bot under {bot.Name}";

    public static string BeneathName(BotSummary bot) => $"Bots under {bot.Name}";

    public static int Hue(BotSummary bot) => BotIdentity.Hue(bot.Id);

    #endregion

    #region Private Methods

    // A bot that another bot made says which one, when that bot is still here.
    private string MadeBy(BotSummary bot) =>
        bot.MadeByBotId is { } maker && Bots!.FirstOrDefault(candidate => candidate.Id == maker) is { } made
            ? $" · made by {made.Name}"
            : string.Empty;

    #endregion
}
