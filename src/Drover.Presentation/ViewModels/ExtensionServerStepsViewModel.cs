namespace Drover.Presentation.ViewModels;

/// <summary>
/// The tool server of an installed extension: fill in the extension's settings and connect it. Giving its tools to a
/// bot is done in the bot's own settings, like any connection's.
/// </summary>
public sealed partial class ExtensionServerStepsViewModel(
    DroverApi api, SessionState session, ILogger<ExtensionServerStepsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private readonly Dictionary<string, string> typed = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Connection), nameof(Shows), nameof(Heading), nameof(ConnectLabel))]
    private ExtensionSummary? extension;

    [ObservableProperty]
    private string headerValue = string.Empty;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool acceptLocal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel))]
    private bool busy;

    #endregion

    #region Properties

    /// <summary>The extension as it is after a step, for the list to show.</summary>
    public event Action<ExtensionSummary>? Changed;

    public static string ToolsLegend => "Tools it suggests giving a bot";

    public static string GrantHint => "You give a tool to a bot in the bot's settings, under Tools.";

    public static string HeaderValueHint => "Stored encrypted. It is not shown again.";

    public ExtensionConnectionSummary Connection => Extension!.Connection!;

    public IReadOnlyList<ExtensionSetting> Settings => Extension!.Parts.Settings;

    /// <summary>The server decides this for every request. Here it only keeps controls a person cannot use off the screen.</summary>
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    public string Heading => $"Tool server: {Connection.Name}";

    public string ConnectLabel => Busy ? "Connecting" : $"Connect {Connection.Name}";

    public string HeaderValueLabel => $"Value of {Connection.HeaderName}";

    /// <summary>The person types the header's whole value when the file leaves it to them.</summary>
    public bool AsksForHeaderValue => Connection.AsksForHeader;

    /// <summary>
    /// The yes to a local address is asked for when the address is local, and also when the address depends on a
    /// setting: then only the server knows, once the settings are filled in, whether it is.
    /// </summary>
    public bool AsksAboutLocal => Connection.IsLocal || Connection.DependsOnSettings;

    public string LocalLabel => Connection.DependsOnSettings
        ? "Yes, connect even if the address is on this server's own network, or is not https."
        : $"Yes, connect to {Connection.Host}. It is on this server's own network, or is not https.";

    public string ConnectLead => ExtensionWords.Auth(Connection.Auth, Connection.HeaderName, Connection.AsksForHeader);

    public string LinkText => Connection.Auth is "oauth" ? "Sign in on the Tools screen" : "Open on the Tools screen";

    public string StatusText => ExtensionWords.ConnectionStatus(Connection);

    public bool StatusIsReady => Connection.Status is "ready";

    public bool ShowsTools => Connection.Tools.Count > 0;

    /// <summary>Which step the row is at.</summary>
    public ServerStep Shows =>
        !Connection.Available ? ServerStep.TurnedOff
        : Connection.ConnectionId is null && !CanEdit ? ServerStep.NotConnectedYet
        : Connection.ConnectionId is null ? ServerStep.Connect
        : ServerStep.Connected;

    #endregion

    #region Public Methods

    /// <summary>Shows the extension as it now is: after a step, or when the list was replaced.</summary>
    public void Show(ExtensionSummary shown) => Extension = shown;

    /// <summary>
    /// What a setting's field holds: what the person typed, then what it was connected with last, then what the
    /// file starts it with. A secret is never given back, so its field starts empty.
    /// </summary>
    public string Value(ExtensionSetting setting) =>
        typed.TryGetValue(setting.Key, out var value) ? value
        : Extension!.FilledSettings.TryGetValue(setting.Key, out var kept) ? kept
        : setting.Default ?? (setting.Type is ExtensionSetting.Toggle ? "false" : string.Empty);

    public void SetValue(ExtensionSetting setting, string value) => typed[setting.Key] = value;

    [RelayCommand]
    public Task Connect() =>
        Step(async () =>
        {
            var request = new ConnectExtensionRequest(AcceptLocal, AsksForHeaderValue ? HeaderValue : null)
            {
                Settings = Settings.ToDictionary(setting => setting.Key, Value),
            };
            var connected = await api.ConnectExtension(session.Space!.Id, Extension!.Id, request);
            HeaderValue = string.Empty;
            typed.Clear();
            Changed?.Invoke(connected);
        });

    #endregion

    #region Private Methods

    private async Task Step(Func<Task> work)
    {
        Busy = true;
        Problem = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionServerStepsViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}

/// <summary>How far a tool server has come from its extension's row.</summary>
public enum ServerStep
{
    TurnedOff,
    NotConnectedYet,
    Connect,
    Connected,
}
