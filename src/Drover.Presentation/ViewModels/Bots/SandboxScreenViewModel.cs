namespace Drover.Presentation.ViewModels.Bots;

/// <summary>
/// A bot's sandbox screen, live, wherever a person looks at it: on the sandbox screen and beside the conversation.
/// Its job: let a person watch what the bot does on its screen and take over the mouse and keyboard when they choose.
/// The picture refreshes on its own every two seconds while it is shown, the sandbox runs, and the app is visible,
/// waits longer after each failure, and is fetched again soon after anything a person sends. It can be enlarged and
/// moved (<see cref="ScreenZoom"/>); a tap still lands on the sandbox pixel under it. A sandbox that is not made yet
/// or is paused is never started by looking: the person starts or resumes it here with one button, and a failure says
/// why in the server's words. The sandbox's state comes from whatever shows the screen, which reads it; this model
/// only reads the picture. The server does everything.
/// </summary>
public sealed partial class SandboxScreenViewModel(
    DroverApi api,
    SessionState session,
    TimeProvider time,
    ILogger<SandboxScreenViewModel> logger) : ObservableObject, IAsyncDisposable
{
    #region Private Fields

    private CancellationTokenSource? stopping;

    private Guid botId;

    private string botName = string.Empty;

    private BotSandboxStatus? status;

    private bool held;

    private bool heldSeen;

    private bool visible = true;

    /// <summary>The person asked for the sandbox to be started or resumed here, and its state does not say running yet.</summary>
    private bool waking;

    /// <summary>The person's last start here failed, so a sandbox left starting can be tried again.</summary>
    private bool wakeFailed;

    private bool enterAfter;

    private string clickMode = "left";

    private string typed = string.Empty;

    private int failures;

    private Task? watching;

    /// <summary>Set when a person sent something, so the next picture comes soon. Set twice, it is still one picture.</summary>
    private TaskCompletionSource soon = NewSoon();

    #endregion

    #region Properties

    public static IReadOnlyList<SegmentOption<string>> ClickModes { get; } =
        [new("left", "Click"), new("right", "Right click"), new("double", "Double click")];

    public static IReadOnlyList<SandboxKey> Keys { get; } =
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

    public static IReadOnlyList<SandboxKey> Combinations { get; } =
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

    public static string ControlLabel => "Send desktop input";

    public static string OpenTerminalLabel => "Open terminal window";

    public static string ClickModeLabel => "Desktop click type";

    public static string ZoomLabel => "Zoom";

    public static string PasswordModeLabel => "Type a password";

    public static string EnterAfterLabel => "Press Enter after typing";

    public static string KeysLabel => "Keys";

    public static string CombinationsLabel => "Key combinations";

    public static string ScrollLabel => "Scroll";

    public Guid BotId
    {
        get => botId;
        set => Set(ref botId, value);
    }

    public string BotName
    {
        get => botName;
        set => Set(ref botName, value);
    }

    /// <summary>The sandbox as it is now. The picture is read only while it runs. Nothing shows until it is given.</summary>
    public BotSandboxStatus? Status
    {
        get => status;
        set
        {
            status = value;
            ParametersSet();
        }
    }

    /// <summary>
    /// The person holds the screen because the bot asked them to take it over. Take control turns on when the hold
    /// begins and off when it ends, so the person and the bot never both drive the screen.
    /// </summary>
    public bool Held
    {
        get => held;
        set
        {
            held = value;
            ParametersSet();
        }
    }

    public string? ScreenProblem { get; private set; }

    public bool Sending { get; private set; }

    /// <summary>Whether clicks and keys go to the sandbox. Off, the person only looks.</summary>
    public bool Control { get; private set; }

    public bool PasswordMode { get; private set; }

    public bool EnterAfter
    {
        get => enterAfter;
        set => Set(ref enterAfter, value);
    }

    public string ClickMode
    {
        get => clickMode;
        set => Set(ref clickMode, value);
    }

    public string Typed
    {
        get => typed;
        set => Set(ref typed, value);
    }

    /// <summary>The screen as the server last sent it, as PNG bytes. Null until one has come.</summary>
    public byte[]? Picture { get; private set; }

    public int PictureWidth { get; private set; } = 1024;

    public int PictureHeight { get; private set; } = 768;

    /// <summary>Where the next keyboard click lands, in the sandbox's own pixels.</summary>
    public (int X, int Y) PointerAt { get; private set; } = (512, 384);

    public ScreenZoom Zoom { get; private set; } = ScreenZoom.Fit;

    /// <summary>
    /// The picture is fetched only while someone can see it and the sandbox runs, or the person asked for it to be
    /// started or resumed: looking alone never resumes a paused one or makes a missing one.
    /// </summary>
    public bool WatchesScreen => visible && status is { Desktop: true } && (waking || status.State is "running");

    public bool DesktopReady => status is { Desktop: true, State: "running" } && Picture is not null;

    public string? ScreenNote => waking || status is null ? null : status.State switch
    {
        "none" when status.StartFailed => "The last start of this sandbox did not finish. Start it here to see why.",
        "none" => "No sandbox yet. It starts when the bot first uses it.",
        "paused" => "The sandbox is paused and its files are kept. Resume it to see the screen.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" => "The sandbox is starting. The screen shows once it runs.",
        _ => SandboxWords.NoScreen(status),
    };

    /// <summary>
    /// The one button that brings the screen up, named for what it does, when a tap is what is missing: no sandbox
    /// yet, a paused one, or one left starting after the person's own start failed.
    /// </summary>
    public string? WakeLabel => (waking || status is not { Desktop: true }) ? null : status.State switch
    {
        "none" => "Start the sandbox",
        "paused" => "Resume the sandbox",
        "starting" when wakeFailed => "Try again",
        _ => null,
    };

    public string WakingWords => status?.State is "paused"
        ? "Resuming the sandbox."
        : "Starting the sandbox. A new one can take a minute.";

    /// <summary>What is said while no picture shows yet and one is on its way. Null when nothing is on its way.</summary>
    public string? ProgressNote => !DesktopReady && WatchesScreen && ScreenProblem is null
        ? waking ? WakingWords : "Getting the screen."
        : null;

    public string ControlWords => Control
        ? "Clicks and keys go to the sandbox."
        : "View only. Turn on Send desktop input to click or type.";

    /// <summary>Said while the person drives the screen and the bot works: both may use the same desktop.</summary>
    public string? WorkingNote => DesktopReady && Control && status is { BotWorking: true }
        ? $"{BotName} has an active run and may use the same desktop."
        : null;

    public string TypeAction => PasswordMode ? "Type password" : EnterAfter ? "Type and press Enter" : "Type text";

    public string TypeLabel => PasswordMode ? "Password" : "Text to type";

    public string TypeHint => PasswordMode
        ? "Focus the remote password prompt first. This sends the password without pressing Enter. The sandbox is shared with the bot."
        : "Text goes to the focused window in the sandbox.";

    public bool CanType => !Sending && Typed.Length > 0;

    public string PointerWords => $"The pointer is at {PointerAt.X}, {PointerAt.Y} of {PictureWidth} by {PictureHeight}.";

    /// <summary>The picture's accessible name while the person drives the screen.</summary>
    public string ControlPictureName =>
        $"{BotName}'s sandbox screen. Click or tap the desktop. With the keyboard, arrow keys move the pointer and Enter clicks.";

    /// <summary>The picture's accessible name while the person only looks.</summary>
    public string ViewPictureName =>
        $"{BotName}'s sandbox screen, as it was a moment ago. It refreshes every two seconds while it is shown.";

    /// <summary>The name of the enlarged picture, which takes the focus so the arrow keys can move the view.</summary>
    public static string EnlargedName => "Enlarged screen. Arrow keys move the view.";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    /// <summary>
    /// Raised after the person started or resumed the sandbox here, or tried to, so whatever shows the screen reads
    /// the state again at once instead of on its timer.
    /// </summary>
    public event Func<Task>? SandboxChanged;

    /// <summary>Raised after text was sent: the text field keeps the focus, so the on-screen keyboard stays up for the next line.</summary>
    public event Action? KeepTyping;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync() => await Stop();

    #endregion

    #region Watching

    /// <summary>Begins refreshing the picture. Called when the screen is shown.</summary>
    public void Start()
    {
        if (watching is not null)
        {
            return;
        }

        stopping = new();
        watching = Watch(stopping.Token);
    }

    /// <summary>Ends the refresh. Called when the screen goes away.</summary>
    public async Task Stop()
    {
        if (stopping is not { } stop)
        {
            return;
        }

        stopping = null;
        await stop.CancelAsync();

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

        watching = null;
        stop.Dispose();
    }

    /// <summary>Called when the app is hidden or shown again.</summary>
    public void VisibilityChanged(bool shown)
    {
        visible = shown;
        Changed();
    }

    /// <summary>
    /// The person asks for the sandbox: the next picture is fetched at once, and asking for it makes a missing sandbox
    /// and resumes a paused one.
    /// </summary>
    public void Wake()
    {
        (waking, wakeFailed, ScreenProblem, failures) = (true, false, null, 0);
        soon.TrySetResult();
        Changed();
    }

    private void ParametersSet()
    {
        if (status is null)
        {
            Changed();
            return;
        }

        if (held != heldSeen)
        {
            (heldSeen, Control) = (held, held);
        }

        if (status.State is "running")
        {
            (waking, wakeFailed) = (false, false);
        }
        else if (!waking)
        {
            if (Picture is not null)
            {
                (ScreenProblem, failures) = (null, 0);
            }

            Picture = null;
            typed = string.Empty;
            PasswordMode = false;
            Control = held;
        }

        Changed();
    }

    private async Task Watch(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            if (WatchesScreen && !Sending)
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

    private static TaskCompletionSource NewSoon() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RefreshPicture(CancellationToken stop)
    {
        // The first picture after the person asked for the sandbox: whatever shows the screen reads the state again
        // once it has come or failed, since it has just changed.
        var woke = waking && Picture is null;

        try
        {
            var png = await api.GetBotSandboxScreen(SpaceId, BotId, wake: woke, stop);
            if (!woke && status?.State is not "running")
            {
                return;
            }

            (PictureWidth, PictureHeight) = SizeOf(png) ?? throw new InvalidDataException("The screen's answer was not a PNG picture.");
            Picture = png;
            (ScreenProblem, failures) = (null, 0);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen closed while the sandbox picture was being fetched");
            return;
        }
        catch (Exception exception)
        {
            if (!woke && status?.State is not "running")
            {
                return;
            }

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
                ScreenProblem = Failures.Sentence(exception);
            }
            else
            {
                failures++;
                ScreenProblem = $"{Failures.Sentence(exception)} The screen is tried again shortly.";
            }
        }

        Changed();

        if (woke)
        {
            await TellSandboxChanged();
        }
    }

    private async Task TellSandboxChanged()
    {
        foreach (var told in SandboxChanged?.GetInvocationList().Cast<Func<Task>>() ?? [])
        {
            await told();
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

    public void SetControl(bool on)
    {
        Control = on;
        if (!on && PasswordMode)
        {
            SetPasswordMode(false);
        }

        Changed();
    }

    public void SetPasswordMode(bool on)
    {
        typed = string.Empty;
        PasswordMode = on;
        Changed();
    }

    public Task<bool> OpenTerminal() => Send(new SandboxInputRequest("open_terminal"));

    /// <summary>
    /// A click or a tap on the picture: it clicks the sandbox pixel under it. <paramref name="offsetX"/> and
    /// <paramref name="offsetY"/> are from the top left corner of the picture's frame, which was
    /// <paramref name="shownWidth"/> by <paramref name="shownHeight"/> when it was tapped.
    /// </summary>
    public Task Tap(double offsetX, double offsetY, double shownWidth, double shownHeight)
    {
        var (width, height) = shownWidth > 0 && shownHeight > 0 ? (shownWidth, shownHeight) : (PictureWidth, PictureHeight);
        PointerAt = ScreenPoint.FromShown(offsetX, offsetY, width, height, PictureWidth, PictureHeight, Zoom);
        return ClickAtPointer();
    }

    /// <summary>A click made with the keyboard: it clicks where the keyboard put the pointer.</summary>
    public Task ClickAtPointer() => Send(new SandboxInputRequest(
        "click",
        X: PointerAt.X,
        Y: PointerAt.Y,
        Button: ClickMode is "right" ? "right" : "left",
        Twice: ClickMode is "double"));

    /// <summary>An arrow key moves the pointer, and the view with it. True when the key was one that moves it.</summary>
    public bool MovePointer(string key, bool far)
    {
        if (ScreenPoint.Move(PointerAt, key, far, PictureWidth, PictureHeight) is not { } moved)
        {
            return false;
        }

        PointerAt = moved;
        var (x, y) = ScreenPoint.Share(PointerAt, PictureWidth, PictureHeight);
        Zoom = Zoom.Showing(x, y);
        Changed();
        return true;
    }

    /// <summary>
    /// Types the text and, with Press Enter after typing on, presses Enter after it, so a command is one action. The field
    /// clears and keeps the focus, so the on-screen keyboard stays up for the next line.
    /// </summary>
    public async Task TypeText()
    {
        if (typed.Length == 0 || Sending)
        {
            return;
        }

        if (PasswordMode)
        {
            var password = typed;
            typed = string.Empty;
            await Send(new SandboxInputRequest("password", Text: password));
            KeepTyping?.Invoke();
            return;
        }

        if (await Send(new SandboxInputRequest("type", Text: typed)))
        {
            typed = string.Empty;
            Changed();

            if (enterAfter)
            {
                await Send(new SandboxInputRequest("key", Keys: ["enter"]));
            }
        }

        KeepTyping?.Invoke();
    }

    public Task<bool> Press(IReadOnlyList<string> pressed) => Send(new SandboxInputRequest("key", Keys: pressed));

    public Task<bool> Scroll(bool up) =>
        Send(new SandboxInputRequest("scroll", Direction: up ? "up" : "down", Amount: 3, X: PointerAt.X, Y: PointerAt.Y));

    /// <summary>Sends one input, then shows the screen it left, so the person sees what it did.</summary>
    private async Task<bool> Send(SandboxInputRequest input)
    {
        if (!DesktopReady || Sending || (!Control && input.Kind is not "open_terminal"))
        {
            return false;
        }

        Sending = true;
        ScreenProblem = null;
        var sent = false;
        Changed();

        try
        {
            await api.SendBotSandboxInput(SpaceId, BotId, input);
            sent = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "An input to the sandbox was refused");
            ScreenProblem = Failures.Sentence(exception, "Check that the server is running.");
        }

        Sending = false;

        if (sent)
        {
            soon.TrySetResult();
        }

        Changed();
        return sent;
    }

    #endregion

    #region Zoom

    public void ZoomIn() => Show(Zoom.ZoomedIn());

    public void ZoomOut() => Show(Zoom.ZoomedOut());

    public void FitScreen() => Show(ScreenZoom.Fit);

    /// <summary>An arrow key moves the enlarged view. True when the key was one that moves it.</summary>
    public bool PanView(string key, bool far)
    {
        if (Zoom.Moved(key, far) is not { } moved)
        {
            return false;
        }

        Show(moved);
        return true;
    }

    /// <summary>Called when a pinch or a drag has enlarged or moved the picture.</summary>
    public void ViewChanged(double scale, double x, double y) => Show(new ScreenZoom(scale, x, y).Clamped());

    private void Show(ScreenZoom view)
    {
        Zoom = view;
        Changed();
    }

    #endregion

    #region Private Methods

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Changed();
        }
    }

    /// <summary>Whatever shows this model reads all of it again: one change can alter many of its sentences.</summary>
    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
