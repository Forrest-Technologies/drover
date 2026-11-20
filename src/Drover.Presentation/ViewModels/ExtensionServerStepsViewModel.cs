namespace Drover.Presentation.ViewModels;

/// <summary>The tool server of an installed extension: connect it, then choose which of its suggested tools the bot gets.</summary>
public sealed partial class ExtensionServerStepsViewModel(
    DroverApi api, SessionState session, ILogger<ExtensionServerStepsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private readonly HashSet<string> ticked = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Connection), nameof(Shows), nameof(Heading), nameof(ConnectLabel), nameof(GiveLabel))]
    private ExtensionSummary? extension;

    [ObservableProperty]
    private string headerValue = string.Empty;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool acceptLocal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel), nameof(GiveLabel), nameof(CanGive))]
    private bool busy;

    #endregion

    #region Properties

    /// <summary>The extension as it is after a step, for the list to show.</summary>
    public event Action<ExtensionSummary>? Changed;

    public ExtensionConnectionSummary Connection => Extension!.Connection!;

    /// <summary>The server decides this for every request. Here it only keeps controls a person cannot use off the screen.</summary>
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    public string Heading => $"Tool server: {Connection.Name}";

    public string ConnectLabel => Busy ? "Connecting" : $"Connect {Connection.Name}";

    public string GiveLabel => Busy ? "Giving the tools" : $"Give {Extension!.BotName} the ticked tools, asking first";

    public string HeaderValueLabel => $"Value of {Connection.HeaderName}";

    public string LocalLabel => $"Yes, connect to {Connection.Host}. It is on this server's own network, or is not https.";

    public string ToolsLegend => $"Tools suggested for {Extension!.BotName}";

    public string ConnectLead => ExtensionWords.Auth(Connection.Auth, Connection.HeaderName);

    public string LinkText => Connection.Auth is "oauth" ? "Sign in on the Tools screen" : "Open on the Tools screen";

    public string StatusText => ExtensionWords.ConnectionStatus(Connection);

    public bool StatusIsReady => Connection.Status is "ready";

    /// <summary>The tools of a connected server are shown when there is a bot to give them to.</summary>
    public bool ShowsTools => Extension!.BotId is not null && Connection.Tools.Count > 0;

    /// <summary>The button that gives tools is for people who can edit, while a tool is left that could be given.</summary>
    public bool ShowsGive => CanEdit && Connection.Tools.Any(tool => tool is { ToolId: not null, Grant: "off" });

    public bool CanGive => !Busy && ticked.Count > 0;

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

    public bool IsTicked(string tool) => ticked.Contains(tool);

    /// <summary>Whether a tool's box is ticked: one the bot already has is, and one the person ticked is.</summary>
    public bool IsOn(SuggestedToolState tool) => tool.Grant is not "off" || ticked.Contains(tool.Name);

    /// <summary>A tool can be ticked when the server lists it and the bot does not have it yet.</summary>
    public static bool CanTick(SuggestedToolState tool) => tool.ToolId is not null && tool.Grant is "off";

    public void Tick(string tool, bool on)
    {
        if (on)
        {
            ticked.Add(tool);
        }
        else
        {
            ticked.Remove(tool);
        }

        OnPropertyChanged(nameof(CanGive));
    }

    [RelayCommand]
    public Task Connect() =>
        Step(async () =>
        {
            var connected = await api.ConnectExtension(
                session.Space!.Id, Extension!.Id, new ConnectExtensionRequest(AcceptLocal, Connection.Auth is "header" ? HeaderValue : null));
            HeaderValue = string.Empty;
            Changed?.Invoke(connected);
        });

    /// <summary>Gives the bot each ticked tool on ask, one request a tool: the server checks each against the bots above it.</summary>
    [RelayCommand]
    public Task Grant() =>
        Step(async () =>
        {
            var spaceId = session.Space!.Id;
            foreach (var tool in Connection.Tools.Where(tool => tool is { ToolId: not null, Grant: "off" } && ticked.Contains(tool.Name)))
            {
                await api.SetBotToolGrant(spaceId, Extension!.BotId!.Value, tool.ToolId!.Value, "ask");
            }

            ticked.Clear();
            Changed?.Invoke((await api.ListExtensions(spaceId)).Find(listed => listed.Id == Extension!.Id) ?? Extension!);
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
