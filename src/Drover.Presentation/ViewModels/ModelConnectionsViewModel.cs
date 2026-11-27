namespace Drover.Presentation.ViewModels;

/// <summary>
/// The model Connections of the space. Its job: see whether each connection works, add one, test it, replace its key.
/// A change here can change which models listen and speak, so what is known about voice is read again afterwards.
/// </summary>
public sealed partial class ModelConnectionsViewModel(
    DroverApi api,
    SessionState session,
    SpaceVoice voice,
    ILogger<ModelConnectionsViewModel> logger) : ObservableObject, IDisposable
{
    #region Private Fields

    private const string CompatibleKind = NamedProviders.CompatibleKind;

    private readonly Dictionary<Guid, string> problems = [];

    private NamedProviders? named;

    private CancellationTokenSource? poll;

    [ObservableProperty]
    private List<ModelConnectionSummary>? connections;

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

    [ObservableProperty]
    private string? userCode;

    [ObservableProperty]
    private string? verificationAddress;

    [ObservableProperty]
    private bool waitingForApproval;

    #endregion

    #region Properties

    public IReadOnlyDictionary<Guid, string> Problems => problems;

    /// <summary>The provider choice: Venice, the server's named providers, another compatible server.</summary>
    public NamedProviders Named => named ??= MakeNamed();

    public string Kind => Named.Kind;

    public bool IsSignIn => Named.IsSignIn;

    public string SignInName => Named.SignInName;

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
            await Task.WhenAll(Load(), Named.Load());
        }
    }

    public void Dispose() => StopWaiting();

    #endregion

    #region Private Methods

    public static (BadgeKind Kind, string Words) StatusOf(ModelConnectionSummary connection) => connection.Status switch
    {
        "ready" => (BadgeKind.Ok, "Working"),
        "failed" => (BadgeKind.Danger, "Not working"),
        _ => (BadgeKind.Neutral, "Not tested"),
    };

    public string Facts(ModelConnectionSummary connection)
    {
        var kindWords = Named.DisplayNameOf(connection.Kind);
        var key = connection.SignIn is { } signIn
            ? SignInWords(signIn)
            : connection.HasKey ? "key stored" : "no key";
        var checkedWords = connection.LastCheckedAt is { } at ? $"checked {at.ToLocalTime():g}" : "never checked";
        return $"{kindWords} · {key} · {checkedWords}";
    }

    public bool NeedsSignIn(ModelConnectionSummary connection) =>
        connection.SignIn is { Status: ModelSignInStatuses.None or ModelSignInStatuses.NeedsSignIn };

    public bool IsSignedIn(ModelConnectionSummary connection) =>
        connection.SignIn?.Status == ModelSignInStatuses.Active;

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
        StopWaiting();
        if (Named.Choose(chosen) is { } address)
        {
            BaseUrl = address;
        }

        AddProblem = null;
        UserCode = null;
        VerificationAddress = null;
    }

    public void ChangeAddress() => Named.RevealAddress();

    public Task RetryProviders() => Named.Load();

    public void StartAdding()
    {
        Adding = true;
        AddProblem = null;
        Notice = null;
        ApiKey = string.Empty;
        BaseUrl = Named.Selected?.Address ?? string.Empty;
        Name = string.Empty;
        UserCode = null;
        VerificationAddress = null;
        WaitingForApproval = false;
    }

    public void CancelAdding()
    {
        StopWaiting();
        Adding = false;
        AddProblem = null;
        ApiKey = string.Empty;
        BaseUrl = Named.Selected?.Address ?? string.Empty;
        Name = string.Empty;
        UserCode = null;
        VerificationAddress = null;
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

    public Task Add() => IsSignIn ? StartSignIn() : AddKey();

    public Task SignIn(ModelConnectionSummary connection)
    {
        if (Named.Choose(connection.Kind) is { } address)
        {
            BaseUrl = address;
        }

        return StartSignIn();
    }

    public async Task SignOut(ModelConnectionSummary connection) =>
        await Change(connection, async () =>
        {
            await api.SignOutOfModelConnection(SpaceId, connection.Id);
            await Load();
            Say(false, $"Signed out of {connection.Name}.");
        });

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

    private async Task AddKey()
    {
        AddingBusy = true;
        AddProblem = null;

        try
        {
            var address = Kind == CompatibleKind ? BaseUrl.Trim() : null;
            var key = string.IsNullOrWhiteSpace(ApiKey) || !Named.ShowsKey ? null : ApiKey.Trim();
            var added = await api.CreateModelConnection(SpaceId, new(Kind, string.IsNullOrWhiteSpace(Name) ? Named.NameFor(address) : Name.Trim(), address, key, null));
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

    private async Task StartSignIn()
    {
        StopWaiting();
        AddingBusy = true;
        AddProblem = null;
        UserCode = null;
        VerificationAddress = null;

        try
        {
            var started = await api.StartModelSignIn(SpaceId, new(Kind, null));
            UserCode = started.UserCode;
            VerificationAddress = started.Address;
            WaitingForApproval = true;
            AddingBusy = false;
            await WaitForApproval(started.AttemptId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            AddProblem = Failures.Sentence(exception, "Check that the server is running.");
            WaitingForApproval = false;
        }
        finally
        {
            AddingBusy = false;
        }
    }

    private async Task WaitForApproval(Guid attemptId)
    {
        poll = new CancellationTokenSource();
        var token = poll.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                var progress = await api.ReadModelSignIn(SpaceId, attemptId, token);

                if (progress.Status == ModelSignInStatuses.Done && progress.Connection is { } made)
                {
                    ReplaceOrAdd(made);
                    voice.Forget();
                    Adding = false;
                    WaitingForApproval = false;
                    UserCode = null;
                    VerificationAddress = null;
                    Say(false, "Signed in. Usage bills the signed-in plan.");
                    return;
                }

                if (progress.Status is ModelSignInStatuses.Refused or ModelSignInStatuses.Expired)
                {
                    AddProblem = progress.Status == ModelSignInStatuses.Expired
                        ? "Sign-in took too long. Start it again."
                        : "Sign-in was cancelled.";
                    WaitingForApproval = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace("The sign-in wait on the {Screen} screen ended", nameof(ModelConnectionsViewModel));
            WaitingForApproval = false;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            AddProblem = Failures.Sentence(exception, "Check that the server is running.");
            WaitingForApproval = false;
        }
    }

    private void StopWaiting()
    {
        poll?.Cancel();
        poll?.Dispose();
        poll = null;
        WaitingForApproval = false;
    }

    private void ReplaceOrAdd(ModelConnectionSummary changed)
    {
        Connections ??= [];
        var index = Connections.FindIndex(connection => connection.Id == changed.Id);

        if (index >= 0)
        {
            Connections[index] = changed;
        }
        else
        {
            Connections.Add(changed);
        }
    }

    private static string SignInWords(ModelSignInSummary signIn) => signIn.Status switch
    {
        ModelSignInStatuses.Active => string.IsNullOrWhiteSpace(signIn.AccountLabel) ? "signed in" : signIn.AccountLabel,
        ModelSignInStatuses.NeedsSignIn => "needs sign-in",
        _ => "not signed in",
    };

    private NamedProviders MakeNamed()
    {
        var made = new NamedProviders(api, logger);
        made.Changed += () => OnPropertyChanged(string.Empty);
        return made;
    }

    #endregion
}
