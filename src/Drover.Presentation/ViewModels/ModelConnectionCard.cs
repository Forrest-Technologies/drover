namespace Drover.Presentation.ViewModels;

/// <summary>One model of a connection's catalog, as a line of its list: the name, the id, and the state when it is not available.</summary>
public sealed record CatalogLine(string DisplayName, string Model, string? StateNote)
{
    #region Properties

    public bool HasStateNote => StateNote is not null;

    #endregion
}

/// <summary>
/// One model connection of the screen: whether it works, what can be done with it, and its model list while that is
/// shown. The screen's view model keeps the state and tells each card what it is.
/// </summary>
public sealed partial class ModelConnectionCard(ModelConnectionsViewModel owner, ModelConnectionSummary connection) : ObservableObject
{
    #region Private Fields

    private static readonly string[] everything =
    [
        nameof(Connection), nameof(Name), nameof(Facts), nameof(StatusKind), nameof(StatusWords), nameof(Problem), nameof(HasProblem),
        nameof(ChangePending), nameof(NotChangePending), nameof(CanEdit), nameof(ShowRefresh), nameof(Replacing), nameof(NotReplacing), nameof(Catalog),
        nameof(ShowCatalog), nameof(ShowNoModels), nameof(CatalogLabel),
    ];

    [ObservableProperty]
    private string newKey = string.Empty;

    private ModelConnectionSummary current = connection;

    #endregion

    #region Properties

    public Guid Id => current.Id;

    public ModelConnectionSummary Connection => current;

    public string Name => current.Name;

    public string Facts => ModelConnectionsViewModel.Facts(current);

    public BadgeKind StatusKind => ModelConnectionsViewModel.StatusOf(current).Kind;

    public string StatusWords => ModelConnectionsViewModel.StatusOf(current).Words;

    public string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    public bool ChangePending { get; private set; }

    public bool NotChangePending => !ChangePending;

    public bool CanEdit { get; private set; }

    public bool ShowRefresh { get; private set; }

    public bool Replacing { get; private set; }

    public bool NotReplacing => !Replacing;

    public IReadOnlyList<CatalogLine>? Catalog { get; private set; }

    public bool ShowCatalog => Catalog is { Count: > 0 };

    public bool ShowNoModels => Catalog is { Count: 0 };

    public string CatalogLabel => $"Models from {Name}";

    public static string NoModelsNote => "This connection has no listed models. You can enter a model ID in the bot’s Model section.";

    #endregion

    #region Public Methods

    /// <summary>Takes what the screen's state says about this connection, and says what changed.</summary>
    public void Update(
        ModelConnectionSummary summary, string? problem, bool changePending, bool canEdit, bool showRefresh, bool replacing,
        IReadOnlyList<CatalogLine>? catalog)
    {
        current = summary;
        (Problem, ChangePending, CanEdit, ShowRefresh, Replacing, Catalog) = (problem, changePending, canEdit, showRefresh, replacing, catalog);
        foreach (var name in everything)
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    public Task ViewCatalog() => owner.ShowCatalog(this);

    [RelayCommand]
    public Task RefreshModels() => owner.RefreshCatalog(this);

    [RelayCommand]
    public Task Test() => owner.Test(this);

    [RelayCommand]
    public void StartReplacing() => owner.StartReplacing(this);

    [RelayCommand]
    public void CancelReplacing() => owner.CancelReplacing(this);

    [RelayCommand]
    public Task SaveKey() => owner.ReplaceKey(this);

    /// <summary>Forgets a key that was typed and not sent.</summary>
    public void ForgetKey() => NewKey = string.Empty;

    #endregion
}
