namespace Drover.Presentation.ViewModels;

/// <summary>
/// The appearance choices the account menu opens: the theme, the mode, and the accent, each with the one in use
/// marked. A choice applies at once and the choices stay open, so a person can try one after another. Colors always
/// change at once. Shapes, sizes and type need the open screen made again, which would drop what a person typed
/// there and has not saved or sent, so while the screen holds such work they wait until it is left.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private static readonly (ThemeMode Mode, string Name)[] themeNames =
    [
        (ThemeMode.Dark, "Dark"),
        (ThemeMode.Light, "Light"),
        (ThemeMode.Black, "Black"),
        (ThemeMode.System, "Match this device"),
    ];

    private readonly ThemeModel theme;

    private readonly Navigator navigator;

    private readonly DroverApi api;

    private readonly SessionState session;

    private readonly ILogger<AppearanceViewModel> logger;

    private Guid? loadedForUser;

    private Guid? loadingForUser;

    private Guid? themesForSpace;

    private long choiceVersion;

    private bool loadPending;

    private bool pendingSync;

    private bool syncRunning;

    private bool disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitingNote))]
    private bool shapesWait;

    [ObservableProperty]
    private string? syncNote;

    #endregion

    #region Constructors

    // A traditional constructor, because the choices listen to the theme from the moment they exist.
#pragma warning disable IDE0290
    public AppearanceViewModel(
        ThemeModel theme,
        Navigator navigator,
        DroverApi api,
        SessionState session,
        ILogger<AppearanceViewModel> logger)
#pragma warning restore IDE0290
    {
        this.theme = theme;
        this.navigator = navigator;
        this.api = api;
        this.session = session;
        this.logger = logger;
        theme.PropertyChanged += ThemeChanged;
        session.Changed += SessionChanged;
        session.ReachChanged += ReachChanged;
        SessionChanged();
    }

    #endregion

    #region Properties

    /// <summary>True while the choices are showing.</summary>
    public bool Open => theme.AppearanceOpen;

    /// <summary>
    /// Said under the choices while the open screen keeps the shapes it was made with, and nothing otherwise.
    /// </summary>
    public string? WaitingNote => ShapesWait
        ? "Shapes and sizes change when you leave this screen, so your unsaved work is kept."
        : null;

    public IReadOnlyList<ThemeOption> Themes =>
        [.. themeNames.Select(name => new ThemeOption(name.Mode, name.Name, theme.Choice.Mode == name.Mode))];

    public static string OfferedLegend => "From this space's extensions";

    /// <summary>The themes the app has, the default first.</summary>
    public IReadOnlyList<LookOption> Looks =>
    [
        .. BuiltInThemes.All.Select(look =>
            new LookOption(look.Id, look.Name, look.Intent, look.Id == theme.Chosen.Id)),
    ];

    /// <summary>The themes the space's extensions offer, each with the extension it comes from. Empty when there are none.</summary>
    public IReadOnlyList<LookOption> OfferedLooks =>
    [
        .. theme.Offered.Select(offered =>
            new LookOption(offered.Theme.Id, offered.Theme.Name, offered.Theme.Intent, offered.Theme.Id == theme.Chosen.Id) { Source = offered.Source }),
    ];

    /// <summary>
    /// The accents the theme in use offers, its own color first. One that is not offered is not listed, so a theme
    /// with no other accent lists only its own.
    /// </summary>
    public IReadOnlyList<AccentOption> AccentChoices =>
    [
        .. Accents.For(theme.Chosen).Select(accent => new AccentOption(
            accent.Id,
            accent.Name,
            theme.Chosen.WithAccent(accent.Id).Dark.Colors["ember"],
            theme.Chosen.WithAccent(accent.Id).Light.Colors["ember"],
            ReferenceEquals(theme.Chosen.WithAccent(accent.Id), theme.Theme))),
    ];

    #endregion

    #region Public Methods

    public void Dispose()
    {
        disposed = true;
        theme.PropertyChanged -= ThemeChanged;
        session.Changed -= SessionChanged;
        session.ReachChanged -= ReachChanged;
    }

    [RelayCommand]
    public void ChooseTheme(ThemeMode mode)
    {
        theme.Choose(mode);
        ChoiceChanged();
    }

    [RelayCommand]
    public void ChooseLook(string themeId)
    {
        theme.ChooseTheme(themeId);
        ChoiceChanged();
    }

    [RelayCommand]
    public void ChooseAccent(string accentId)
    {
        theme.ChooseAccent(accentId);
        ChoiceChanged();
    }

    [RelayCommand]
    public void RetrySync() => StartSync(ignoreReach: true);

    [RelayCommand]
    public void Close() => theme.AppearanceOpen = false;

    /// <summary>
    /// Asked by the shell once a choice has put new shapes and sizes in place: whether to make the open screen again
    /// from them now. Not while the screen holds work that making it again would drop: something a person typed in
    /// it, as the shell sees, or what the screen itself says it holds. It then waits until the screen is left.
    /// </summary>
    public bool MakesScreenAgain(bool somethingTyped)
    {
        ShapesWait = somethingTyped || navigator.HoldsWork?.Invoke() is true;
        return !ShapesWait;
    }

    /// <summary>Told by the shell when a screen has been made, which is always from the values in place.</summary>
    public void ScreenMade() => ShapesWait = false;

    #endregion

    #region Private Methods

    private void ThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(ThemeModel.AppearanceOpen))
        {
            OnPropertyChanged(nameof(Open));

            // What a space offers changes when an extension is imported or removed, so it is read again when the choices open.
            if (theme.AppearanceOpen)
            {
                _ = LoadSpaceThemes(always: true);
            }

            return;
        }

        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(Looks));
        OnPropertyChanged(nameof(OfferedLooks));
        OnPropertyChanged(nameof(AccentChoices));
    }

    private void ChoiceChanged()
    {
        choiceVersion++;
        if (session.Current is null)
        {
            pendingSync = false;
            SyncNote = null;
            return;
        }

        loadPending = false;
        pendingSync = true;
        StartSync();
    }

    private void SessionChanged()
    {
        var userId = session.Current?.UserId;
        if (userId is null)
        {
            themesForSpace = null;
            loadedForUser = null;
            loadPending = false;
            pendingSync = false;
            SyncNote = null;
            return;
        }

        if (loadedForUser == userId)
        {
            // The same person in another space: what that space offers is read, and the choice is left alone.
            _ = LoadSpaceThemes(always: false);
            return;
        }

        loadedForUser = userId;
        loadPending = true;
        pendingSync = false;
        SyncNote = null;
        _ = LoadFromServer(userId.Value, choiceVersion);
    }

    private void ReachChanged()
    {
        if (session.Unreachable || session.Current is null)
        {
            return;
        }

        if (loadPending)
        {
            _ = LoadFromServer(session.Current.UserId, choiceVersion);
        }

        if (pendingSync)
        {
            StartSync();
        }
    }

    private async Task LoadFromServer(Guid userId, long version)
    {
        if (session.Unreachable)
        {
            loadPending = true;
            return;
        }

        if (loadingForUser == userId)
        {
            return;
        }

        loadingForUser = userId;
        try
        {
            // First what the space offers, so a choice that names one of its themes is found when it is applied.
            await LoadSpaceThemes(always: false);
            var setting = await api.GetAppearance();
            if (!disposed && session.Current?.UserId == userId && choiceVersion == version && setting is not null)
            {
                theme.ApplyFromServer(setting);
            }

            if (session.Current?.UserId == userId)
            {
                loadPending = false;
            }
        }
        catch (Exception exception)
        {
            if (session.Current?.UserId == userId && choiceVersion == version)
            {
                loadPending = true;
            }

            logger.LogWarning(
                exception,
                "The appearance choice could not be loaded for user {UserId}",
                userId);
        }
        finally
        {
            if (loadingForUser == userId)
            {
                loadingForUser = null;
            }
        }
    }

    /// <summary>
    /// Reads the themes the open space offers and hands them to the theme model, which checks each. A space that
    /// was read already is read again only when asked to. A failure leaves what is offered as it was: the theme in
    /// use stays drawn from the copy this device keeps.
    /// </summary>
    private async Task LoadSpaceThemes(bool always)
    {
        if (session.Space?.Id is not { } spaceId || session.Unreachable || (!always && themesForSpace == spaceId))
        {
            return;
        }

        try
        {
            var offered = await api.ListSpaceThemes(spaceId);
            if (!disposed && session.Space?.Id == spaceId)
            {
                themesForSpace = spaceId;
                theme.Offer(offered);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The themes the space offers could not be loaded");
        }
    }

    private void StartSync(bool ignoreReach = false)
    {
        if (syncRunning || !pendingSync || session.Current is null || (!ignoreReach && session.Unreachable))
        {
            return;
        }

        syncRunning = true;
        _ = FlushPending(ignoreReach);
    }

    private async Task FlushPending(bool ignoreReach)
    {
        var failed = false;
        try
        {
            while (!disposed && pendingSync && session.Current is not null && (ignoreReach || !session.Unreachable))
            {
                pendingSync = false;
                var userId = session.Current.UserId;
                var choice = theme.Choice;

                try
                {
                    await api.SetAppearance(ToSetting(choice));
                    if (disposed)
                    {
                        return;
                    }

                    if (session.Current?.UserId != userId)
                    {
                        ignoreReach = false;
                        continue;
                    }

                    SyncNote = null;
                }
                catch (Exception exception)
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (session.Current?.UserId != userId)
                    {
                        ignoreReach = false;
                        continue;
                    }

                    pendingSync = true;
                    failed = true;
                    logger.LogWarning(
                        exception,
                        "The appearance choice could not be saved for user {UserId}",
                        userId);
                    return;
                }
            }
        }
        finally
        {
            syncRunning = false;
            if (failed && !disposed && session.Current is not null)
            {
                SyncNote = "Saved on this device. Drover could not save it to your account.";
            }
        }
    }

    private static AppearanceSetting ToSetting(ThemeChoice choice) =>
        new(choice.ThemeId, choice.Mode.ToString().ToLowerInvariant(), choice.Accent);

    #endregion
}
