namespace Drover.Presentation.ViewModels;

/// <summary>
/// The model Connections of the space. Its job: see whether each connection works, add one, test it, replace its key.
/// A change here can change which models listen and speak, so what is known about voice is read again afterwards.
/// </summary>
public sealed partial class ModelConnectionsViewModel(
    DroverApi api,
    SessionState session,
    SpaceVoice voice,
    IModelSignInListener listener,
    ILogger<ModelConnectionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string CompatibleKind = NamedProviders.CompatibleKind;

    private readonly Dictionary<Guid, string> problems = [];

    private NamedProviders? named;

    private ModelConnectionSummary? settingsFor;

    private ModelSignInFlow? signIn;

    /// <summary>The connection whose card shows the sign-in in progress, when it was started from the card.</summary>
    [ObservableProperty]
    private Guid? signingInTo;

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
    private bool editingSettings;

    [ObservableProperty]
    private bool settingsBusy;

    [ObservableProperty]
    private string? settingsProblem;

    [ObservableProperty]
    private bool confirmingClear;

    [ObservableProperty]
    private string? addressProblem;

    [ObservableProperty]
    private string? addKeyProblem;

    #endregion

    #region Properties

    public IReadOnlyDictionary<Guid, string> Problems => problems;

    /// <summary>The provider choice and what the form shows for it, drawn from the kinds the server offers.</summary>
    public NamedProviders Named => named ??= MakeNamed();

    public string Kind => Named.Kind;

    /// <summary>The sign-in shown in place of the key form, or on a connection's card when its person signs in again.</summary>
    public ModelSignInFlow SignIn => signIn ??= MakeSignIn();

    /// <summary>True when the provider chosen in "Add a connection" is a kind each person signs in to.</summary>
    public bool ShowsSignIn => Named.SignIn is not null;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    public bool ChangePending => Busy is not null || AddingBusy || SettingsBusy;

    /// <summary>What an admin is saying about one model: the form behind "Edit what this model can do".</summary>
    public ModelSettingsForm Settings { get; } = new();

    public ModelConnectionSummary? SettingsConnection => settingsFor;

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Overrides

    public async Task Open()
    {
        if (await session.Load() is not null)
        {
            await Task.WhenAll(Load(), Named.Load());

            // The server's first choice can itself be a sign-in kind.
            if (Named.SignIn is { } kind)
            {
                SignIn.Show(kind);
            }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Whether the operator switched this connection's kind off: the server lists the kinds it offers, and the
    /// connection's is not among them. Never said while the list is unread.
    /// </summary>
    public bool IsOff(ModelConnectionSummary connection) =>
        Named.State == NamedProvidersState.Loaded && !Named.Offers(connection.Kind);

    public (BadgeKind Kind, string Words) StatusOf(ModelConnectionSummary connection) => IsOff(connection)
        ? (BadgeKind.Neutral, "Off")
        : connection.Status switch
        {
            "ready" => (BadgeKind.Ok, "Working"),
            "failed" => (BadgeKind.Danger, "Not working"),
            _ => (BadgeKind.Neutral, "Not tested"),
        };

    /// <summary>The sentence under the name of a connection whose kind the operator switched off, or null.</summary>
    public string? OffWords(ModelConnectionSummary connection) =>
        IsOff(connection) ? $"The operator of this server switched {KindName(connection)} off." : null;

    public string Facts(ModelConnectionSummary connection)
    {
        var kindWords = KindName(connection);
        var key = connection.HasKey ? "key stored" : "no key";
        var checkedWords = connection.LastCheckedAt is { } at ? $"checked {at.ToLocalTime():g}" : "never checked";
        return $"{kindWords} · {key} · {checkedWords}";
    }

    /// <summary>The sign-in kind of a connection, as the server describes it, or null for a key connection.</summary>
    public ModelProviderKindResponse? SignInKindOf(ModelConnectionSummary connection) =>
        connection.SignIn is null ? null : Named.SignInKindOf(connection.Kind);

    /// <summary>
    /// What a sign-in connection's card says of the caller's own sign-in: in use with the account it is, ended, or not
    /// made. Never anyone else's.
    /// </summary>
    public string? SignInFacts(ModelConnectionSummary connection)
    {
        var kind = SignInKindOf(connection);
        var provider = kind?.SignInName ?? connection.Name;
        var noun = kind?.PersonalPlan is false ? "account" : "plan";

        return connection.SignIn?.Status switch
        {
            ModelSignInStatuses.Active => connection.SignIn.AccountLabel is { Length: > 0 } label ? $"Using {provider} {noun} · {label}" : $"Using {provider} {noun}",
            ModelSignInStatuses.NeedsSignIn => $"Your {provider} sign-in has ended.",
            ModelSignInStatuses.SignedOut => $"Signed out of your {provider} {noun}.",
            ModelSignInStatuses.None => $"Sign in with your own {provider} {noun} to use it.",
            _ => null,
        };
    }

    /// <summary>The words of a sign-in connection's own action on its card, or null when its sign-in is in use.</summary>
    public string? SignInActionWords(ModelConnectionSummary connection) =>
        connection.SignIn?.Status switch
        {
            ModelSignInStatuses.NeedsSignIn => "Sign in again",
            ModelSignInStatuses.None or ModelSignInStatuses.SignedOut => $"Continue with {SignInKindOf(connection)?.SignInName ?? connection.Name}",
            _ => null,
        };

    /// <summary>The sign-in state's one action in "Add a connection": start, cancel, or move to a provider that takes a key.</summary>
    public async Task ActOnSignIn(Func<string, Task> open)
    {
        switch (SignIn.Action)
        {
            case ModelSignInAction.Continue or ModelSignInAction.TryAgain:
                await SignIn.Start(SpaceId, open);
                await SettleSignIn();
                break;
            case ModelSignInAction.Cancel:
                SignIn.Cancel();
                break;
            case ModelSignInAction.AddKey:
                SigningInTo = null;
                ChooseProvider(Named.KeyChoice);
                break;
        }
    }

    /// <summary>Starts the caller's own sign-in from a connection's card, at once: "Sign in again" or "Continue with".</summary>
    public async Task SignInAgain(ModelConnectionSummary connection, Func<string, Task> open)
    {
        if (SignInKindOf(connection) is not { } kind)
        {
            return;
        }

        SigningInTo = connection.Id;
        SignIn.Show(kind);

        if (SignIn.Action is ModelSignInAction.Continue)
        {
            await ActOnSignIn(open);
        }
    }

    /// <summary>Signs the caller out of their own sign-in, with no confirmation, and reads the list again.</summary>
    public Task SignOut(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            await api.SignOutOfModelConnection(SpaceId, connection.Id);
            Connections = [.. await api.ListModelConnections(SpaceId)];
            Say(false, $"Signed out of {connection.Name}.");
        });

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

    private string KindName(ModelConnectionSummary connection) =>
        Named.Kinds.FirstOrDefault(kind => kind.Kind == connection.Kind)?.DisplayName ?? connection.Kind;

    public void ChooseProvider(string chosen)
    {
        if (Named.Choose(chosen) is { } address)
        {
            BaseUrl = address;
        }

        ApiKey = string.Empty;
        if (Named.SignIn is { } kind)
        {
            SigningInTo = null;
            SignIn.Show(kind);
        }

        AddProblem = null;
        AddressProblem = null;
        AddKeyProblem = null;
    }

    public void ChangeAddress() => Named.RevealAddress();

    public Task RetryProviders() => Named.Load();

    public void StartAdding()
    {
        Adding = true;
        AddProblem = null;
        AddressProblem = null;
        AddKeyProblem = null;
        Notice = null;
        ApiKey = string.Empty;
        BaseUrl = Named.Selected is { AddressBlank: null } selected ? selected.Address : string.Empty;
        Name = string.Empty;
    }

    public void CancelAdding()
    {
        Adding = false;
        AddProblem = null;
        ApiKey = string.Empty;
        BaseUrl = Named.Selected is { AddressBlank: null } selected ? selected.Address : string.Empty;
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
        // A sign-in kind is connected by signing in, never added with a key.
        if (ShowsSignIn)
        {
            return;
        }

        AddProblem = null;
        (AddressProblem, var missingKey) = Named.Missing(BaseUrl, ApiKey);
        if (AddressProblem is not null || missingKey is not null || Kind.Length == 0)
        {
            AddKeyProblem = missingKey;
            return;
        }

        AddKeyProblem = null;
        AddingBusy = true;

        try
        {
            var address = Named.TakesAddress ? Named.BlankAddress ?? BaseUrl.Trim() : null;
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

    public Task ShowCatalog(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            ShownCatalog = connection.Id;
            Catalog = null;
            Catalog = await api.ListModels(SpaceId, connection.Id);
        });

    /// <summary>What the catalog says a model does with pictures, wherever that came from: the provider or an admin.</summary>
    public static string? VisionWords(ModelCatalogResponse entry) => entry.Capabilities.Vision ? "reads pictures" : null;

    public void OpenSettings(ModelConnectionSummary connection, ModelCatalogResponse entry)
    {
        Settings.Open(entry);
        settingsFor = connection;
        SettingsProblem = null;
        ConfirmingClear = false;
        EditingSettings = true;
    }

    public void CancelSettings()
    {
        EditingSettings = false;
        ConfirmingClear = false;
        SettingsProblem = null;
    }

    /// <summary>Sends what the admin changed. A value that is out of range stays on the form with its sentence.</summary>
    public async Task SaveSettings()
    {
        if (settingsFor is not { } connection || !Settings.Check())
        {
            OnPropertyChanged(string.Empty);
            return;
        }

        await Mutate(async () =>
        {
            var changed = await api.SetModelSettings(SpaceId, connection.Id, Settings.Request());
            Catalog = ReplaceRow(changed);
            EditingSettings = false;
            Say(false, $"{changed.DisplayName} was updated.");
        });
    }

    public void AskUseProvidersValues() => ConfirmingClear = true;

    public void KeepSettings() => ConfirmingClear = false;

    /// <summary>Clears everything set for the model, so what the provider reported is in effect again.</summary>
    public async Task UseProvidersValues()
    {
        if (settingsFor is not { } connection || Settings.Entry is not { } entry)
        {
            return;
        }

        await Mutate(async () =>
        {
            await api.ClearModelSettings(SpaceId, connection.Id, entry.Model);
            Catalog = [.. await api.ListModels(SpaceId, connection.Id)];
            ConfirmingClear = false;
            EditingSettings = false;
            Say(false, $"{entry.DisplayName} uses the provider's values.");
        });
    }

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

    private IReadOnlyList<ModelCatalogResponse> ReplaceRow(ModelCatalogResponse changed) =>
        Catalog is null || Catalog.All(row => row.Model != changed.Model)
            ? [.. (Catalog ?? []), changed]
            : [.. Catalog.Select(row => row.Model == changed.Model ? changed : row)];

    /// <summary>A change made from the sheet: its problem shows in the sheet, not under the connection.</summary>
    private async Task Mutate(Func<Task> action)
    {
        SettingsBusy = true;
        SettingsProblem = null;
        Notice = null;

        try
        {
            await action();
            voice.Forget();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnectionsViewModel));
            SettingsProblem = Failures.Sentence(exception, "Check that the server is running.");
            ConfirmingClear = false;
        }
        finally
        {
            SettingsBusy = false;
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

    /// <summary>A sign-in that is done closes the form and shows the connection with the person's own sign-in.</summary>
    private async Task SettleSignIn()
    {
        if (SignIn.Stage is not ModelSignInStage.Done)
        {
            return;
        }

        Adding = false;
        SigningInTo = null;
        await Load();
        Say(false, SignIn.Sentence ?? string.Empty);
    }

    private ModelSignInFlow MakeSignIn()
    {
        var made = new ModelSignInFlow(api, listener, logger);
        made.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
        return made;
    }

    private NamedProviders MakeNamed()
    {
        var made = new NamedProviders(api, logger);
        made.Changed += () => OnPropertyChanged(string.Empty);
        return made;
    }

    #endregion
}
