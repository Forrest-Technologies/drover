namespace Drover.Ui.Pages.Bots;

/// <summary>
/// A bot's sandbox. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart
/// it. The state and the power controls sit at the top; the screen (for a sandbox with one) and a terminal are tabs.
/// The screen refreshes on its own every two seconds while it is shown, the sandbox runs, and the page is visible,
/// and waits longer after each failure. The server does everything; this page asks and shows.
/// </summary>
public sealed partial class BotSandbox(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    IJSRuntime js,
    ILogger<BotSandbox> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string ScreenView = "screen";

    private const string TerminalView = "terminal";

    private const string ModulePath = "./_content/Drover.Ui/Pages/Bots/BotSandbox.razor.js";

    /// <summary>The state is read again every this many pictures, so a change made elsewhere shows.</summary>
    private const int StatusEvery = 5;

    private static readonly SegmentOption<string>[] clickModes = [new("left", "Click"), new("right", "Right click"), new("double", "Double click")];

    private static readonly SandboxKey[] keys =
    [
        new("Enter", "Enter", ["enter"]),
        new("Tab", "Tab", ["tab"]),
        new("Esc", "Escape", ["escape"]),
        new("Backspace", "Backspace", ["backspace"]),
        new("Left", "Arrow left", ["left"]),
        new("Up", "Arrow up", ["up"]),
        new("Down", "Arrow down", ["down"]),
        new("Right", "Arrow right", ["right"]),
    ];

    private static readonly SandboxKey[] combinations =
    [
        new("Ctrl+C", "Copy", ["ctrl", "c"]),
        new("Ctrl+V", "Paste", ["ctrl", "v"]),
        new("Ctrl+X", "Cut", ["ctrl", "x"]),
        new("Ctrl+Z", "Undo", ["ctrl", "z"]),
        new("Ctrl+A", "Select all", ["ctrl", "a"]),
        new("Ctrl+L", "Address bar", ["ctrl", "l"]),
        new("Alt+Tab", "Next window", ["alt", "tab"]),
        new("Alt+F4", "Close window", ["alt", "f4"]),
    ];

    private readonly CancellationTokenSource disposal = new();

    private readonly List<CommandEntry> entries = [];

    private BotSummary? bot;

    private BotSandboxStatus? status;

    private Guid? shownBotId;

    private string? view;

    private string? loadProblem;

    private string? actionProblem;

    private string? screenProblem;

    private bool notPermitted;

    private bool missing;

    private bool busy;

    private bool sending;

    private bool running;

    private bool control;

    private bool rebuilding;

    private bool turningOff;

    private bool visible = true;

    private bool arrowsKept;

    private string clickMode = "left";

    private string typed = string.Empty;

    private string command = string.Empty;

    private string? picture;

    private int pictureWidth = 1024;

    private int pictureHeight = 768;

    private (int X, int Y) pointer = (512, 384);

    private int failures;

    private Task? watching;

    private ElementReference pictureButton;

    private ElementReference pictureImage;

    private IJSObjectReference? module;

    private DotNetObjectReference<BotSandbox>? self;

    #endregion

    #region Properties

    [Parameter]
    public Guid BotId { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private IReadOnlyList<TabItem> Views =>
        status?.Desktop is true ? [new(ScreenView, "Screen"), new(TerminalView, "Terminal")] : [new(TerminalView, "Terminal")];

    private bool HasOne => status?.State is "running" or "paused" or "starting";

    /// <summary>The picture is fetched only while someone can see it and the sandbox runs: looking at a paused one would resume it.</summary>
    private bool WatchesScreen => visible && (view ?? ScreenView) is ScreenView && status is { Desktop: true, State: "running" };

    private string? PowerHint => status?.State switch
    {
        "none" => "Nothing to pause, rebuild, or turn off yet.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" => "The sandbox is starting.",
        _ => null,
    };

    private string? ScreenNote => status?.State switch
    {
        "none" => "No sandbox yet. It starts when the bot first uses it, or when you run a command in the terminal.",
        "paused" => "The sandbox is paused and its files are kept. Resume it to see the screen.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" => "The sandbox is starting.",
        _ => null,
    };

    private string ControlWords => control
        ? "You have control. A tap on the screen clicks there, in the sandbox."
        : "View only. Turn on Take control to click and type in the sandbox.";

    private string TerminalHint => status?.State is "none"
        ? "A command runs in the sandbox as bash reads it. The first one starts the sandbox."
        : "A command runs in the sandbox as bash reads it, for up to five minutes.";

    private string PointerStyle
    {
        get
        {
            var (left, top) = ScreenPoint.Percent(pointer, pictureWidth, pictureHeight);
            return string.Create(CultureInfo.InvariantCulture, $"left: {left:0.##}%; top: {top:0.##}%;");
        }
    }

    private string PointerWords => $"The pointer is at {pointer.X}, {pointer.Y} of {pictureWidth} by {pictureHeight}.";

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await disposal.CancelAsync();

        if (watching is not null)
        {
            await watching;
        }

        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("stopWatching");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                logger.LogDebug("The page was gone before the sandbox screen's script was released");
            }
        }

        self?.Dispose();
        disposal.Dispose();
    }

    #endregion

    #region Overrides

    protected override async Task OnParametersSetAsync()
    {
        if (shownBotId != BotId && await session.Load() is not null)
        {
            shownBotId = BotId;
            await Load();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (status is { Available: true, HasSandbox: true } && watching is null)
        {
            await StartWatching();
        }

        if (control && picture is not null && !arrowsKept && module is not null)
        {
            arrowsKept = true;

            try
            {
                await module.InvokeVoidAsync("keepArrows", pictureButton);
            }
            catch (JSException exception)
            {
                logger.LogWarning(exception, "The arrow keys over the sandbox screen may scroll the page");
            }
        }
    }

    #endregion

    #region Loading

    private async Task Load()
    {
        (bot, status, loadProblem, notPermitted, missing, picture) = (null, null, null, false, false, null);

        try
        {
            bot = await api.GetBot(SpaceId, BotId);
            status = await api.GetBotSandbox(SpaceId, BotId);
            view ??= status.Desktop ? ScreenView : TerminalView;
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The {Screen} screen is not open to this person", nameof(BotSandbox));
            notPermitted = true;
        }
        catch (DroverApiException absent) when (absent.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(absent, "The {Screen} screen was asked for a bot that is not there", nameof(BotSandbox));
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandbox));
            loadProblem = Failures.Sentence(exception);
        }
    }

    #endregion

    #region Watching

    private async Task StartWatching()
    {
        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference?>("import", ModulePath);
            self ??= DotNetObjectReference.Create(this);

            if (module is not null)
            {
                await module.InvokeVoidAsync("watchVisibility", self);
            }
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The sandbox screen's script did not load; the screen refreshes whether the page is visible or not");
        }

        watching = Watch(disposal.Token);
    }

    /// <summary>Called by the page's script when the page is hidden or shown again.</summary>
    [JSInvokable]
    public async Task VisibilityChanged(bool shown)
    {
        visible = shown;
        await InvokeAsync(StateHasChanged);
    }

    private async Task Watch(CancellationToken stop)
    {
        var ticks = 0;

        while (!stop.IsCancellationRequested)
        {
            if (visible && WatchesScreen && !sending)
            {
                await RefreshPicture(stop);
            }

            if (visible && ++ticks % StatusEvery == 0)
            {
                await RefreshStatus(stop);
            }

            try
            {
                await Task.Delay(SandboxWords.NextPictureAfter(failures), stop);
            }
            catch (OperationCanceledException stopped)
            {
                logger.LogDebug(stopped, "The sandbox screen stopped refreshing");
                return;
            }
        }
    }

    private async Task RefreshPicture(CancellationToken stop)
    {
        try
        {
            var png = await api.GetBotSandboxScreen(SpaceId, BotId, stop);
            (pictureWidth, pictureHeight) = SizeOf(png) ?? (pictureWidth, pictureHeight);
            picture = $"data:image/png;base64,{Convert.ToBase64String(png)}";
            (screenProblem, failures) = (null, 0);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The page closed while the sandbox screen was being fetched");
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox screen could not be fetched");
            failures++;
            screenProblem = $"{Failures.Sentence(exception)} The screen is tried again shortly.";
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshStatus(CancellationToken stop)
    {
        try
        {
            status = await api.GetBotSandbox(SpaceId, BotId, stop);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The page closed while the sandbox state was being read");
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox state could not be read again");
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>The width and height a PNG says it has, from its header.</summary>
    private static (int Width, int Height)? SizeOf(byte[] png) =>
        png.Length >= 24 && png[12] == 'I' && png[13] == 'H' && png[14] == 'D' && png[15] == 'R'
            ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) is var (width, height) && width > 0 && height > 0
                ? (width, height)
                : null
            : null;

    #endregion

    #region Power

    private Task Pause() => Power(() => api.PauseBotSandbox(SpaceId, BotId), "The sandbox is paused. Its files are kept.");

    private Task Resume() => Power(() => api.ResumeBotSandbox(SpaceId, BotId), "The sandbox is running again.");

    private async Task Rebuild()
    {
        rebuilding = false;
        picture = null;
        await Power(() => api.RebuildBotSandbox(SpaceId, BotId), "The sandbox was rebuilt. It is a fresh one.");
    }

    private async Task TurnOff()
    {
        turningOff = false;
        picture = null;
        await Power(() => api.TurnOffBotSandbox(SpaceId, BotId), "The sandbox is turned off. A new one starts on its next use.");
    }

    private async Task Power(Func<Task<BotSandboxStatus>> change, string outcome)
    {
        busy = true;
        actionProblem = null;

        try
        {
            status = await change();
            failures = 0;
            toasts.Show(outcome);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandbox));
            actionProblem = Failures.Sentence(exception);
        }

        busy = false;
    }

    #endregion

    #region Taking Control

    private void SetControl(bool on)
    {
        control = on;
        arrowsKept = false;
    }

    private async Task ClickPicture(MouseEventArgs click)
    {
        // A click with no pointer behind it came from the keyboard: it clicks where the keyboard put the pointer.
        if (click.Detail > 0)
        {
            var (shownWidth, shownHeight) = await ShownSize();
            pointer = ScreenPoint.FromShown(click.OffsetX, click.OffsetY, shownWidth, shownHeight, pictureWidth, pictureHeight);
        }

        await Send(new SandboxInputRequest(
            "click",
            X: pointer.X,
            Y: pointer.Y,
            Button: clickMode is "right" ? "right" : "left",
            Twice: clickMode is "double"));
    }

    private void MovePointer(KeyboardEventArgs key)
    {
        if (ScreenPoint.Move(pointer, key.Key, key.ShiftKey, pictureWidth, pictureHeight) is { } moved)
        {
            pointer = moved;
        }
    }

    private async Task TypeText()
    {
        if (typed.Length == 0)
        {
            return;
        }

        if (await Send(new SandboxInputRequest("type", Text: typed)))
        {
            typed = string.Empty;
        }
    }

    private Task<bool> Press(IReadOnlyList<string> pressed) => Send(new SandboxInputRequest("key", Keys: pressed));

    private Task<bool> Scroll(bool up) => Send(new SandboxInputRequest("scroll", Direction: up ? "up" : "down", Amount: 3, X: pointer.X, Y: pointer.Y));

    /// <summary>Sends one input, then shows the screen it left, so the person sees what it did.</summary>
    private async Task<bool> Send(SandboxInputRequest input)
    {
        sending = true;
        screenProblem = null;
        var sent = false;

        try
        {
            await api.SendBotSandboxInput(SpaceId, BotId, input);
            sent = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "An input to the sandbox was refused");
            screenProblem = Failures.Sentence(exception);
        }

        sending = false;

        if (sent && WatchesScreen)
        {
            await RefreshPicture(disposal.Token);
        }

        return sent;
    }

    /// <summary>The size the picture is shown at. Without the script, as large as the screen itself.</summary>
    private async Task<(double Width, double Height)> ShownSize()
    {
        try
        {
            if (module is not null && await module.InvokeAsync<double[]?>("size", pictureImage) is [var width, var height] && width > 0 && height > 0)
            {
                return (width, height);
            }
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The size of the sandbox screen could not be read");
        }

        return (pictureWidth, pictureHeight);
    }

    #endregion

    #region Terminal

    private async Task RunCommand()
    {
        if (running || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var entry = new CommandEntry(command);
        entries.Add(entry);
        command = string.Empty;
        running = true;

        try
        {
            entry.Result = await api.RunBotSandboxCommand(SpaceId, BotId, entry.Command);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "A command in the sandbox was refused");
            entry.Problem = Failures.Sentence(exception);
        }

        running = false;
        await RefreshStatus(disposal.Token);
    }

    #endregion

    #region Nested Types

    /// <summary>A key button: the words on it, its accessible name, and the key names it presses together.</summary>
    private sealed record SandboxKey(string Label, string Name, IReadOnlyList<string> Keys);

    /// <summary>One command of this visit, and what it left once it is done.</summary>
    private sealed class CommandEntry(string command)
    {
        #region Properties

        public string Command => command;

        public SandboxCommandResponse? Result { get; set; }

        public string? Problem { get; set; }

        #endregion
    }

    #endregion
}
