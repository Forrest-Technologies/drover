namespace Drover.Presentation.ViewModels;

/// <summary>
/// The model Connections of the space. Its job: see whether each connection works, add one, test it, replace its key.
/// A change here can change which models listen and speak, so what is known about voice is read again afterwards.
/// </summary>
public sealed partial class ModelConnectionsViewModel(
    DroverApi api,
    SessionState session,
    SpaceVoice voice,
    ILogger<ModelConnectionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string VeniceKind = "venice";

    private const string CompatibleKind = "openai_compatible";

    private const string OpenAIKind = "openai";

    public static readonly SegmentOption<string>[] Providers =
    [
        new(VeniceKind, "Venice"),
        new(OpenAIKind, "OpenAI"),
        new(CompatibleKind, "Compatible endpoint"),
    ];

    private readonly Dictionary<Guid, string> problems = [];

    [ObservableProperty]
    private List<ModelConnectionSummary>? connections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyHint))]
    private string kind = VeniceKind;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private Guid? shownCatalog;

    [ObservableProperty]
    private IReadOnlyList<ModelCatalogResponse>? catalog;


    [ObservableProperty]
    private string baseUrl = string.Empty;

    [ObservableProperty]
    private string apiKey = string.Empty;

    [ObservableProperty]
    private string newKey = string.Empty;

    [ObservableProperty]
    private Guid? replacing;

    [ObservableProperty]
    private Guid? busy;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? addProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    private bool noticeIsProblem;

    [ObservableProperty]
    private bool adding;

    [ObservableProperty]
    private bool addingBusy;

    [ObservableProperty]
    private string? keyProblem;

    #endregion

    #region Properties

    public IReadOnlyDictionary<Guid, string> Problems => problems;

    /// <summary>The one sentence under the key field, for the chosen provider.</summary>
    public string KeyHint => Kind switch
    {
        CompatibleKind => "Leave this empty if the server needs no key.",
        OpenAIKind => "Paste an API key from your OpenAI account.",
        _ => "Made in your Venice account, under API keys.",
    };

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    public bool ChangePending => Busy is not null || AddingBusy;

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

    public async Task Load()
    {
        LoadProblem = null;

        try
        {
            Connections = [.. await api.ListModelConnections(SpaceId)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    public void ChooseProvider(string chosen)
    {
        Kind = chosen;
        AddProblem = null;
    }

    public void StartAdding()
    {
        Adding = true;
        AddProblem = null;
        Notice = null;
        ApiKey = string.Empty;
        BaseUrl = string.Empty;
        Name = string.Empty;
    }

    public void CancelAdding()
    {
        Adding = false;
        AddProblem = null;
        ApiKey = string.Empty;
        BaseUrl = string.Empty;
        Name = string.Empty;
    }

    public void StartReplacing(ModelConnectionSummary connection)
    {
        problems.Remove(connection.Id);
        KeyProblem = null;
        Replacing = connection.Id;
        NewKey = string.Empty;
    }

    public void CancelReplacing()
    {
        if (Replacing is { } id)
        {
            problems.Remove(id);
        }

        KeyProblem = null;
        Replacing = null;
        NewKey = string.Empty;
    }

    public async Task Add()
    {
        AddingBusy = true;
        AddProblem = null;

        try
        {
            var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
            var key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
            var added = await api.CreateModelConnection(SpaceId, new(Kind, string.IsNullOrWhiteSpace(Name) ? NameFor(address) : Name.Trim(), address, key, null));
            Connections!.Add(added);
            voice.Forget();
            Adding = false;
            ApiKey = string.Empty;
            await Test(added);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            AddProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            AddingBusy = false;
        }
    }

    public Task ShowCatalog(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            ShownCatalog = connection.Id;
            Catalog = null;
            Catalog = await api.ListModels(SpaceId, connection.Id);
        });

    /// <summary>
    /// Whether a person may say a model of this connection reads pictures. Only a compatible endpoint takes it: it
    /// lists models and nothing about them. A provider that reports its own capabilities offers no toggle.
    /// </summary>
    public bool CanMarkVision(ModelConnectionSummary connection) => CanEdit && connection.Kind == CompatibleKind;

    public static string VisionLabel(ModelCatalogResponse entry) => $"{entry.DisplayName} reads pictures";

    /// <summary>Says, for a model whose reading of pictures nobody can change here, what the catalog says.</summary>
    public static string? VisionWords(ModelCatalogResponse entry) => entry.Capabilities.Vision ? "reads pictures" : null;

    public Task SetVision(ModelConnectionSummary connection, ModelCatalogResponse entry, bool readsImages) =>
        Change(connection, async () =>
        {
            var changed = await api.SetModelVision(SpaceId, connection.Id, new(entry.Model, readsImages));
            Catalog = [.. (Catalog ?? []).Select(row => row.Model == changed.Model ? changed : row)];
            Say(false, readsImages
                ? $"{entry.DisplayName} is marked as reading pictures. Pictures attached for a bot that uses it go to it as pictures."
                : $"{entry.DisplayName} is no longer marked as reading pictures.");
        });

    public void HideCatalog()
    {
        ShownCatalog = null;
        Catalog = null;
    }

    public Task RefreshCatalog(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var result = await api.RefreshModels(SpaceId, connection.Id);
            Catalog = result.Models;
            Say(!result.Succeeded, result.Message ?? (result.Succeeded ? "Model list refreshed." : "The provider could not refresh the model list."));
        });

    public Task Test(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var result = await api.TestModelConnection(SpaceId, connection.Id);
            Replace(result.Connection);
            Say(!result.Succeeded, result.Message);
        });

    public async Task ReplaceKey(ModelConnectionSummary connection)
    {
        KeyProblem = null;
        await Change(connection, async () =>
        {
            Replace(await api.ReplaceModelKey(SpaceId, connection.Id, new(NewKey.Trim())));
            NewKey = string.Empty;
            Replacing = null;
            Say(false, $"The key for {connection.Name} is replaced. Test the connection to check it.");
        });

        if (problems.Remove(connection.Id, out var reason))
        {
            KeyProblem = reason;
        }
    }

    private async Task Change(ModelConnectionSummary connection, Func<Task> action)
    {
        Busy = connection.Id;
        Notice = null;
        problems.Remove(connection.Id);

        try
        {
            await action();
            voice.Forget();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            problems[connection.Id] = Failures.Sentence(exception, "Check that the server is running.");
            Notice = null;
        }
        finally
        {
            Busy = null;
        }
    }

    private void Replace(ModelConnectionSummary changed)
    {
        var index = Connections!.FindIndex(connection => connection.Id == changed.Id);
        if (index >= 0)
        {
            Connections[index] = changed;
        }
    }

    private void Say(bool isProblem, string words)
    {
        NoticeIsProblem = isProblem;
        Notice = words;
    }

    private string NameFor(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : Providers.First(option => option.Value == Kind).Label;

    #endregion
}
