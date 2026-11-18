namespace Drover.Presentation.ViewModels;

/// <summary>
/// The model connections of the space. Its job: see whether each connection works, add one, test it, replace its key.
/// </summary>
public sealed partial class ModelConnectionsViewModel(
    DroverApi api,
    SessionState session,
    ILogger<ModelConnectionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string VeniceKind = "venice";

    private const string CompatibleKind = "openai_compatible";

    private static readonly SegmentOption<string>[] providers =
    [
        new(VeniceKind, "Venice"),
        new(CompatibleKind, "Compatible endpoint"),
    ];

    private static readonly string[] stateNames =
    [
        nameof(ShowAddButton), nameof(AddButtonEnabled), nameof(ShowFailed), nameof(ShowLoading), nameof(ShowEmptyToAdd),
        nameof(ShowEmptyReadOnly), nameof(ShowList), nameof(CanEdit), nameof(ChangePending), nameof(AddFormEnabled),
    ];

    private readonly Dictionary<Guid, string> problems = [];

    private List<ModelConnectionSummary>? connections;

    private Guid? shownCatalog;

    private IReadOnlyList<ModelCatalogResponse>? catalog;

    private Guid? replacing;

    private Guid? busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompatible), nameof(ShowVenice))]
    private string kind = VeniceKind;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string baseUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? addProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    private NoteKind noticeKind = NoteKind.Ok;

    [ObservableProperty]
    private bool adding;

    [ObservableProperty]
    private bool addingBusy;

    #endregion

    #region Properties

    public static string Title => "Model connections";

    public static string PageTitle => "Model connections · Drover";

    public static string AddLabel => "Add a model connection";

    public static string LoadingNote => "Loading the model connections.";

    public static string LoadFailedTitle => "Drover could not load the model connections";

    public static string EmptyTitle => "No model connection yet";

    public static string EmptyCause => "Bots think with a model you bring. Add a connection, then choose it in a bot's Model section.";

    public static string EmptyReadOnlyNote => "This space has no model connections yet. An owner or admin of the space can add one.";

    public static IReadOnlyList<SegmentOption<string>> Providers => providers;

    /// <summary>The cards of the list, one for each connection.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ModelConnectionCard> Cards { get; } = [];

    public bool ShowCompatible => Kind == CompatibleKind;

    public bool ShowVenice => !ShowCompatible;

    public string AddSubmitLabel => AddingBusy ? "Adding" : "Add and test";

    public bool AddFormEnabled => !AddingBusy;

    public bool ChangePending => busy is not null || AddingBusy;

    public bool AddButtonEnabled => !ChangePending;

    public bool ShowAddButton => CanEdit && connections is { Count: > 0 } && !Adding;

    public bool ShowFailed => LoadProblem is not null;

    public bool ShowLoading => LoadProblem is null && connections is null;

    public bool ShowEmptyToAdd => LoadProblem is null && connections is { Count: 0 } && !Adding && CanEdit;

    public bool ShowEmptyReadOnly => LoadProblem is null && connections is { Count: 0 } && !Adding && !CanEdit;

    public bool ShowList => LoadProblem is null && connections is { Count: > 0 };

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

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
            connections = [.. await api.ListModelConnections(SpaceId)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            LoadProblem = Failures.Sentence(exception);
        }

        Show();
    }

    [RelayCommand]
    public void StartAdding()
    {
        Adding = true;
        AddProblem = null;
        Notice = null;
        ApiKey = string.Empty;
        BaseUrl = string.Empty;
        Name = string.Empty;
        Show();
    }

    [RelayCommand]
    public void CancelAdding()
    {
        Adding = false;
        Show();
    }

    [RelayCommand]
    public async Task Add()
    {
        AddingBusy = true;
        AddProblem = null;
        Show();

        try
        {
            var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
            var key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
            var added = await api.CreateModelConnection(SpaceId, new(Kind, string.IsNullOrWhiteSpace(Name) ? NameFor(address) : Name.Trim(), address, key, null));
            connections!.Add(added);
            Adding = false;
            ApiKey = string.Empty;
            Show();
            await Test(added);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            AddProblem = Failures.Sentence(exception);
        }
        finally
        {
            AddingBusy = false;
            Show();
        }
    }

    public Task ShowCatalog(ModelConnectionCard card) =>
        Change(card.Connection, async () =>
        {
            shownCatalog = card.Id;
            catalog = null;
            catalog = await api.ListModels(SpaceId, card.Id);
        });

    public Task RefreshCatalog(ModelConnectionCard card) =>
        Change(card.Connection, async () =>
        {
            var result = await api.RefreshModels(SpaceId, card.Id);
            catalog = result.Models;
            Say(!result.Succeeded, result.Message ?? (result.Succeeded ? "Model list refreshed." : "The provider could not refresh the model list."));
        });

    public Task Test(ModelConnectionCard card) => Test(card.Connection);

    public void StartReplacing(ModelConnectionCard card)
    {
        replacing = card.Id;
        foreach (var other in Cards)
        {
            other.ForgetKey();
        }

        Show();
    }

    public void CancelReplacing(ModelConnectionCard card)
    {
        replacing = null;
        card.ForgetKey();
        Show();
    }

    public Task ReplaceKey(ModelConnectionCard card) =>
        Change(card.Connection, async () =>
        {
            Replace(await api.ReplaceModelKey(SpaceId, card.Id, new(card.NewKey.Trim())));
            card.ForgetKey();
            replacing = null;
            Say(false, $"The key for {card.Name} is replaced. Test the connection to check it.");
        });

    public static (BadgeKind Kind, string Words) StatusOf(ModelConnectionSummary connection) => connection.Status switch
    {
        "ready" => (BadgeKind.Ok, "Working"),
        "failed" => (BadgeKind.Danger, "Not working"),
        _ => (BadgeKind.Neutral, "Not tested"),
    };

    public static string Facts(ModelConnectionSummary connection)
    {
        var kindWords = connection.Kind == CompatibleKind ? "compatible endpoint" : connection.Kind;
        var key = connection.HasKey ? "key stored" : "no key";
        var checkedWords = connection.LastCheckedAt is { } at ? $"checked {at.ToLocalTime():g}" : "never checked";
        return $"{kindWords} · {key} · {checkedWords}";
    }

    #endregion

    #region Private Methods

    private Task Test(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var result = await api.TestModelConnection(SpaceId, connection.Id);
            Replace(result.Connection);
            Say(!result.Succeeded, result.Message);
        });

    private async Task Change(ModelConnectionSummary connection, Func<Task> action)
    {
        busy = connection.Id;
        Notice = null;
        problems.Remove(connection.Id);
        Show();

        try
        {
            await action();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            problems[connection.Id] = Failures.Sentence(exception);
            Notice = null;
        }
        finally
        {
            busy = null;
            Show();
        }
    }

    private void Replace(ModelConnectionSummary changed)
    {
        var index = connections!.FindIndex(connection => connection.Id == changed.Id);
        if (index >= 0)
        {
            connections[index] = changed;
        }
    }

    private void Say(bool isProblem, string words)
    {
        NoticeKind = isProblem ? NoteKind.Danger : NoteKind.Ok;
        Notice = words;
    }

    private static string NameFor(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : "Venice";

    // Choosing another provider forgets what the last attempt said.
    partial void OnKindChanged(string value) => AddProblem = null;

    partial void OnAddingBusyChanged(bool value) => OnPropertyChanged(nameof(AddSubmitLabel));

    // Gives each card what the screen's state says about its connection, and tells the page what to show.
    private void Show()
    {
        SyncCards();
        foreach (var stateName in stateNames)
        {
            OnPropertyChanged(stateName);
        }
    }

    private void SyncCards()
    {
        var wanted = connections ?? [];
        var changePending = ChangePending;

        for (var index = 0; index < wanted.Count; index++)
        {
            var connection = wanted[index];
            var card = Cards.FirstOrDefault(existing => existing.Id == connection.Id) ?? new ModelConnectionCard(this, connection);
            card.Update(
                connection, problems.GetValueOrDefault(connection.Id), changePending, CanEdit, CanEdit && shownCatalog == connection.Id,
                replacing == connection.Id, shownCatalog == connection.Id ? CatalogLinesOf(catalog) : null);

            if (index >= Cards.Count)
            {
                Cards.Add(card);
            }
            else if (Cards[index].Id != connection.Id)
            {
                Cards.Remove(card);
                Cards.Insert(index, card);
            }
        }

        while (Cards.Count > wanted.Count)
        {
            Cards.RemoveAt(Cards.Count - 1);
        }
    }

    private static IReadOnlyList<CatalogLine>? CatalogLinesOf(IReadOnlyList<ModelCatalogResponse>? entries) =>
        entries is null
            ? null
            : [.. entries.Select(entry => new CatalogLine(entry.DisplayName, entry.Model, entry.State is "available" ? null : $"({entry.State})"))];

    #endregion
}
