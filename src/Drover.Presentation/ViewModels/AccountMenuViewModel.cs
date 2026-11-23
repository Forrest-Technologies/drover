namespace Drover.Presentation.ViewModels;

/// <summary>
/// The account menu: the theme, the ways to the settings screens, what the app is, and signing out. The same menu is
/// in the bar of a wide window and, compact, in the dock of a narrow one.
/// </summary>
public sealed partial class AccountMenuViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private static readonly (ThemeChoice Choice, string Name)[] themeNames =
    [
        (ThemeChoice.Dark, "Dark theme"),
        (ThemeChoice.Light, "Light theme"),
        (ThemeChoice.System, "Match this device"),
    ];

    private static readonly TimeSpan devicePatience = TimeSpan.FromSeconds(2);

    private readonly SessionState session;

    private readonly ThemeModel theme;

    private readonly ToastService toasts;

    private readonly Navigator navigator;

    private readonly DroverApi api;

    private readonly IPushRegistration? push;

    private readonly IDesktopUpdates updates;

    private readonly ILogger<AccountMenuViewModel> logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(AccessibleLabel))]
    private bool compact;

    [ObservableProperty]
    private bool notificationsOpen;

    [ObservableProperty]
    private bool updatesOpen;

    [ObservableProperty]
    private bool aboutOpen;

    #endregion

    #region Constructors

    // A traditional constructor, because the menu listens to the session and the theme from the moment it exists.
#pragma warning disable IDE0290
    public AccountMenuViewModel(
        SessionState session,
        ThemeModel theme,
        ToastService toasts,
        Navigator navigator,
        DroverApi api,
        ILogger<AccountMenuViewModel> logger,
        IDesktopUpdates? updates = null,
        IPushRegistration? push = null)
#pragma warning restore IDE0290
    {
        this.session = session;
        this.theme = theme;
        this.toasts = toasts;
        this.navigator = navigator;
        this.api = api;
        this.logger = logger;
        this.push = push;
        this.updates = updates ?? new UnsupportedDesktopUpdates();
        session.Changed += SessionChanged;
        theme.PropertyChanged += ThemeChanged;
        this.updates.Changed += UpdatesChanged;
        _ = this.updates.Start();
    }

    #endregion

    #region Properties

    /// <summary>The words on the menu's button: the person's name, or "Account" where there is only room for an icon.</summary>
    public string Label => Compact ? "Account" : session.Current?.DisplayName ?? "Account";

    /// <summary>A fuller name for the compact button, which shows no name.</summary>
    public string? AccessibleLabel =>
        Compact && session.Current is { } person ? $"Account for {person.DisplayName}" : null;

    public IReadOnlyList<ThemeOption> Themes =>
        [.. themeNames.Select(name => new ThemeOption(name.Choice, name.Name, theme.Choice == name.Choice))];

    /// <summary>The status screen is for the people who run the space.</summary>
    public bool ShowsStatus => session.Space?.Role is "owner" or "admin";

    public bool ShowsUpdates => updates.State.Supported;

    public bool UpdateChecksEnabled => updates.State.ChecksEnabled;

    public string UpdatesLabel => updates.State is { Phase: DesktopUpdatePhase.Available, Version: { } version }
        ? $"Update {version} ready"
        : "Updates";

    public string UpdatesTitle => updates.State is { Phase: DesktopUpdatePhase.Available, Version: { } version }
        ? $"Drover {version} is ready"
        : "Desktop updates";

    public string UpdatesDetail => updates.State switch
    {
        // An update already running stays visible when checks are turned off; the setting only stops new checks.
        { Phase: DesktopUpdatePhase.Downloading } => "Downloading and checking the package.",
        { Phase: DesktopUpdatePhase.Installing } => "The installer is opening. Drover will restart when it finishes.",
        { ChecksEnabled: false } => "This installation does not check for updates.",
        { Phase: DesktopUpdatePhase.Checking } => "Checking the signed release feed.",
        { Phase: DesktopUpdatePhase.Available } => "The package signature will be checked before installation.",
        { Phase: DesktopUpdatePhase.Manual, VerifiedPackage: { } package } =>
            $"The package signature is valid. Install the checked file at {package}. Drover does not open a privileged installer on this system.",
        { Phase: DesktopUpdatePhase.Failed, Problem: { } problem } => problem,
        _ => "Drover checks the signed release feed when it starts.",
    };

    public bool CanCheckUpdates => updates.State is
    {
        ChecksEnabled: true, Phase: not DesktopUpdatePhase.Checking
        and not DesktopUpdatePhase.Downloading and not DesktopUpdatePhase.Installing
    };

    public bool CanInstallUpdate => updates.State.Phase is DesktopUpdatePhase.Available;

    public string InstallLabel => OperatingSystem.IsLinux() ? "Download" : "Download and restart";

    #endregion

    #region Public Methods

    public void Dispose()
    {
        session.Changed -= SessionChanged;
        theme.PropertyChanged -= ThemeChanged;
        updates.Changed -= UpdatesChanged;
    }

    [RelayCommand]
    public void ChooseTheme(ThemeChoice choice) => theme.Choose(choice);

    [RelayCommand]
    public void Open(string address) => navigator.NavigateTo(address);

    [RelayCommand]
    public void OpenNotifications() => NotificationsOpen = true;

    /// <summary>What the app is, and the diagnostic log to attach to a bug report.</summary>
    [RelayCommand]
    public void OpenAbout() => AboutOpen = true;

    [RelayCommand]
    public void OpenUpdates() => UpdatesOpen = true;

    public void SetUpdateChecks(bool enabled) => updates.SetChecks(enabled);

    public async Task CheckUpdates()
    {
        await updates.Check();
    }

    public async Task InstallUpdate()
    {
        await updates.Install();
    }

    [RelayCommand]
    public async Task SignOut()
    {
        try
        {
            await TurnOffThisDevice();
            await session.End();
            navigator.NavigateTo(Addresses.SignIn);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(AccountMenuViewModel));
            toasts.Show($"You are still signed in. {Failures.Sentence(exception, "Check that the server is running.")}", ToastKind.Error);
        }
    }

    #endregion

    #region Private Methods

    private void SessionChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(AccessibleLabel));
        OnPropertyChanged(nameof(ShowsStatus));
    }

    private void ThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change) =>
        OnPropertyChanged(nameof(Themes));

    private void UpdatesChanged()
    {
        OnPropertyChanged(nameof(ShowsUpdates));
        OnPropertyChanged(nameof(UpdateChecksEnabled));
        OnPropertyChanged(nameof(UpdatesLabel));
        OnPropertyChanged(nameof(UpdatesTitle));
        OnPropertyChanged(nameof(UpdatesDetail));
        OnPropertyChanged(nameof(CanCheckUpdates));
        OnPropertyChanged(nameof(CanInstallUpdate));
    }

    /// <summary>
    /// A device signed out of stops being woken for its person: the next person on this device must never be shown
    /// that someone else's bot needs them. Best effort and bounded: a device or server that does not answer in time
    /// never keeps the person signed in.
    /// </summary>
    private async Task TurnOffThisDevice()
    {
        if (push is null)
        {
            return;
        }

        using var patience = new CancellationTokenSource(devicePatience);

        try
        {
            var endpoint = push is IDeviceNotificationSettings
                ? await push.Unsubscribe(patience.Token)
                : (await push.Check(patience.Token)).Endpoint;
            if (endpoint is not null)
            {
                await api.TurnOffPush(endpoint, patience.Token);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception) || exception is OperationCanceledException)
        {
            logger.LogWarning(exception, "This device's notifications could not be turned off at sign-out");
        }
    }

    #endregion
}
