namespace Drover.Ui.Bots;

/// <summary>
/// A bot's sandbox screen, live, wherever a person looks at it: on the sandbox screen and beside the conversation.
/// Its job: let a person watch what the bot does on its screen and take over the mouse and keyboard when they choose.
/// The picture refreshes on its own every two seconds while it is shown, the sandbox runs, and the page is visible,
/// waits longer after each failure, and is fetched again soon after anything a person sends. It can be enlarged and
/// moved (<see cref="ScreenZoom"/>); a tap still lands on the sandbox pixel under it. A sandbox that is not made yet
/// or is paused is never started by looking: the person starts or resumes it here with one button, and a failure says
/// why in the server's words. The sandbox's state comes from whatever shows the screen, which reads it; this component
/// only reads the picture. The server does everything.
/// </summary>
public sealed partial class SandboxScreen(
    DroverApi api,
    SessionState session,
    IJSRuntime js,
    TimeProvider time,
    ILogger<SandboxScreen> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Bots/SandboxScreen.razor.js";

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

    private readonly string pointerWordsId = $"dv-sandbox-pointer-words-{Guid.NewGuid():N}";

    private string? screenProblem;

    private bool sending;

    private bool control;

    private bool heldSeen;

    private bool visible = true;

    /// <summary>The person asked for the sandbox to be started or resumed here, and its state does not say running yet.</summary>
    private bool waking;

    /// <summary>The person's last start here failed, so a sandbox left starting can be tried again.</summary>
    private bool wakeFailed;

    private bool enterAfter;

    private string clickMode = "left";

    private string typed = string.Empty;

    private string? picture;

    private int pictureWidth = 1024;

    private int pictureHeight = 768;

    private (int X, int Y) pointer = (512, 384);

    private ScreenZoom zoom = ScreenZoom.Fit;

    private int failures;

    private Task? watching;

    /// <summary>Set when a person sent something, so the next picture comes soon. Set twice, it is still one picture.</summary>
    private TaskCompletionSource soon = NewSoon();

    private ElementReference pictureButton;

    private ElementReference typeForm;

    private IJSObjectReference? module;

    private DotNetObjectReference<SandboxScreen>? self;

    #endregion

    #region Properties

    [Parameter, EditorRequired]
    public Guid BotId { get; set; }

    [Parameter, EditorRequired]
    public string BotName { get; set; } = string.Empty;

    /// <summary>The sandbox as it is now. The picture is read only while it runs.</summary>
    [Parameter, EditorRequired]
    public BotSandboxStatus Status { get; set; } = null!;

    /// <summary>
    /// Called after the person started or resumed the sandbox here, or tried to, so whatever shows the screen reads
    /// the state again at once instead of on its timer.
    /// </summary>
    [Parameter]
    public EventCallback OnSandboxChanged { get; set; }

    /// <summary>
    /// The person holds the screen because the bot asked them to take it over. Take control turns on when the hold
    /// begins and off when it ends, so the person and the bot never both drive the screen.
    /// </summary>
    [Parameter]
    public bool Held { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    /// <summary>
    /// The picture is fetched only while someone can see it and the sandbox runs, or the person asked for it to be
    /// started or resumed: looking alone never resumes a paused one or makes a missing one.
    /// </summary>
    private bool WatchesScreen => visible && Status.Desktop && (waking || Status.State is "running");

    private string? ScreenNote => waking ? null : Status.State switch
    {
        "none" when Status.StartFailed => "The last start of this sandbox did not finish. Start it here to see why.",
        "none" => "No sandbox yet. It starts when the bot first uses it.",
        "paused" => "The sandbox is paused and its files are kept. Resume it to see the screen.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" => "The sandbox is starting. The screen shows once it runs.",
        _ => SandboxWords.NoScreen(Status),
    };

    /// <summary>
    /// The one button that brings the screen up, named for what it does, when a tap is what is missing: no sandbox
    /// yet, a paused one, or one left starting after the person's own start failed.
    /// </summary>
    private string? WakeLabel => (waking || !Status.Desktop) ? null : Status.State switch
    {
        "none" => "Start the sandbox",
        "paused" => "Resume the sandbox",
        "starting" when wakeFailed => "Try again",
        _ => null,
    };

    private string WakingWords => Status.State is "paused"
        ? "Resuming the sandbox."
        : "Starting the sandbox. A new one can take a minute.";

    private string ControlWords => control
        ? "Clicks and keys go to the sandbox."
        : "View only. Turn on Send desktop input to click or type.";

    private string PointerStyle
    {
        get
        {
            var (left, top) = ScreenPoint.Percent(pointer, pictureWidth, pictureHeight);
            return string.Create(CultureInfo.InvariantCulture, $"left: {left:0.##}%; top: {top:0.##}%;");
        }
    }

    private string PointerWords => $"The pointer is at {pointer.X}, {pointer.Y} of {pictureWidth} by {pictureHeight}.";

    private string FrameClass =>
        $"dv-sandbox-frame{(control ? " dv-sandbox-frame-control" : string.Empty)}{(zoom.IsFit ? string.Empty : " dv-sandbox-frame-zoomed")}";

    /// <summary>The frame keeps the screen's shape, so it can be sized by its height while the keyboard is up.</summary>
    private string FrameStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"aspect-ratio: {pictureWidth} / {pictureHeight}; --dv-sandbox-aspect: {(double)pictureWidth / pictureHeight:0.####};");

    private string LayerStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"transform: translate({zoom.X * 100:0.####}%, {zoom.Y * 100:0.####}%) scale({zoom.Scale:0.####}); --dv-sandbox-zoom: {zoom.Scale:0.####};");

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await disposal.CancelAsync();

        // Closing never fails because of how the refresh ended: the screen is going away either way.
        if (watching is not null)
        {
            try
            {
                await watching;
            }
            catch (Exception ended)
            {
                logger.LogDebug(ended, "The sandbox picture's refresh ended with a failure as it closed");
            }
        }

        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("stop");
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

    // A sandbox that is gone or being replaced shows no picture of the one before it. One the person is starting here
    // keeps its picture until the state catches up and says it runs.
    protected override void OnParametersSet()
    {
        if (Held != heldSeen)
        {
            (heldSeen, control) = (Held, Held);
        }

        if (Status.State is "running")
        {
            (waking, wakeFailed) = (false, false);
        }

        if (!waking && Status.State is "none" or "replacing")
        {
            picture = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await StartWatching();
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
                await module.InvokeVoidAsync("start", self);
            }
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The sandbox screen's script did not load; the screen refreshes whether the page is visible or not and does not pinch");
        }

        watching = Watch(disposal.Token);
    }

    /// <summary>Called by the script when the page is hidden or shown again.</summary>
    [JSInvokable]
    public async Task VisibilityChanged(bool shown)
    {
        visible = shown;
        await InvokeAsync(StateHasChanged);
    }

    private async Task Watch(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            if (WatchesScreen && !sending)
            {
                await RefreshPicture(stop);
            }

            if (!await WaitForNextPicture(stop))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Waits the usual time for the next picture, or, once a person has sent something, only as long as the sandbox
    /// takes to draw it. Pictures are fetched one at a time by this loop alone, so inputs in quick succession never
    /// pile up requests: one is in flight at most, and one more is due after it.
    /// </summary>
    private async Task<bool> WaitForNextPicture(CancellationToken stop)
    {
        var sent = soon.Task;
        var next = Task.Delay(SandboxWords.NextPictureAfter(failures), time, stop);

        if (await Task.WhenAny(next, sent) == sent)
        {
            soon = NewSoon();
            next = Task.Delay(SandboxWords.PictureAfterInput, time, stop);
        }

        try
        {
            await next;
            return true;
        }
        catch (OperationCanceledException stopped)
        {
            logger.LogDebug(stopped, "The sandbox screen stopped refreshing");
            return false;
        }
    }

    /// <summary>
    /// The person asks for the sandbox: the next picture is fetched at once, and asking for it makes a missing sandbox
    /// and resumes a paused one.
    /// </summary>
    private void Wake()
    {
        (waking, wakeFailed, screenProblem, failures) = (true, false, null, 0);
        soon.TrySetResult();
    }

    private static TaskCompletionSource NewSoon() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RefreshPicture(CancellationToken stop)
    {
        // The first picture after the person asked for the sandbox: whatever shows the screen reads the state again
        // once it has come or failed, since it has just changed.
        var woke = waking && picture is null;

        try
        {
            var png = await api.GetBotSandboxScreen(SpaceId, BotId, stop);
            (pictureWidth, pictureHeight) = SizeOf(png) ?? throw new InvalidDataException("The screen's answer was not a PNG picture.");
            picture = $"data:image/png;base64,{Convert.ToBase64String(png)}";
            (screenProblem, failures) = (null, 0);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen closed while the sandbox picture was being fetched");
            return;
        }
        catch (Exception exception)
        {
            // Whatever goes wrong with one picture, it says so and the next one still comes, later each time: only
            // closing the screen ends the refresh.
            logger.Log(
                Failures.IsReportable(exception) ? LogLevel.Information : LogLevel.Warning,
                exception,
                "The sandbox screen could not be fetched");
            if (woke)
            {
                // A start that failed is not tried again on its own: each try asks the provider for a sandbox. The
                // person reads why, and the button is there for another try.
                (waking, wakeFailed) = (false, true);
                screenProblem = Failures.Sentence(exception);
            }
            else
            {
                failures++;
                screenProblem = $"{Failures.Sentence(exception)} The screen is tried again shortly.";
            }
        }

        await InvokeAsync(StateHasChanged);

        if (woke)
        {
            await InvokeAsync(() => OnSandboxChanged.InvokeAsync());
        }
    }

    /// <summary>The width and height a PNG says it has, from its header.</summary>
    private static (int Width, int Height)? SizeOf(byte[] png) =>
        png.Length >= 24 && png[12] == 'I' && png[13] == 'H' && png[14] == 'D' && png[15] == 'R'
            ? (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))) is var (width, height) && width > 0 && height > 0
                ? (width, height)
                : null
            : null;

    #endregion

    #region Taking Control

    private void SetControl(bool on) => control = on;

    private async Task ClickPicture(MouseEventArgs click)
    {
        // A click with no pointer behind it came from the keyboard: it clicks where the keyboard put the pointer.
        if (click.Detail > 0)
        {
            var (shownWidth, shownHeight) = await ShownSize();
            pointer = ScreenPoint.FromShown(click.OffsetX, click.OffsetY, shownWidth, shownHeight, pictureWidth, pictureHeight, zoom);
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
            var (x, y) = ScreenPoint.Share(pointer, pictureWidth, pictureHeight);
            zoom = zoom.Showing(x, y);
        }
    }

    /// <summary>
    /// Types the text and, with Press Enter after typing on, presses Enter after it, so a command is one action. The field
    /// clears and keeps the focus, so the on-screen keyboard stays up for the next line.
    /// </summary>
    private async Task TypeText()
    {
        if (typed.Length == 0 || sending)
        {
            return;
        }

        if (await Send(new SandboxInputRequest("type", Text: typed)))
        {
            typed = string.Empty;

            if (enterAfter)
            {
                await Send(new SandboxInputRequest("key", Keys: ["enter"]));
            }
        }

        await KeepTyping();
    }

    private async Task KeepTyping()
    {
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("keepTyping", typeForm);
            }
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The text field could not keep the focus after sending");
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

        if (sent)
        {
            soon.TrySetResult();
        }

        return sent;
    }

    /// <summary>
    /// The size of the frame the picture is shown in, as it was when the person tapped. Without the script, as large
    /// as the screen itself.
    /// </summary>
    private async Task<(double Width, double Height)> ShownSize()
    {
        try
        {
            if (module is not null && await module.InvokeAsync<double[]?>("size", pictureButton) is [var width, var height] && width > 0 && height > 0)
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

    #region Zoom

    private void ZoomIn() => zoom = zoom.ZoomedIn();

    private void ZoomOut() => zoom = zoom.ZoomedOut();

    private void FitScreen() => zoom = ScreenZoom.Fit;

    private void PanView(KeyboardEventArgs key)
    {
        if (zoom.Moved(key.Key, key.ShiftKey) is { } moved)
        {
            zoom = moved;
        }
    }

    /// <summary>Called by the script when a pinch or a drag has enlarged or moved the picture.</summary>
    [JSInvokable]
    public async Task ViewChanged(double scale, double x, double y)
    {
        zoom = new ScreenZoom(scale, x, y).Clamped();
        await InvokeAsync(StateHasChanged);
    }

    private static string ViewNumber(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion

    #region Nested Types

    /// <summary>A key button: the words on it, its accessible name, and the key names it presses together.</summary>
    private sealed record SandboxKey(string Label, string Name, IReadOnlyList<string> Keys);

    #endregion
}
