namespace Drover.Presentation.ViewModels.Bots;

/// <summary>What the sandbox screen shows under its back link.</summary>
public enum BotSandboxContent
{
    Loading,
    LoadFailed,
    NotPermitted,
    Missing,
    NoProvider,
    NoSandbox,
    Sandbox,
}

/// <summary>
/// A bot's sandbox. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart
/// it. The state and the power controls sit at the top; the screen (for a sandbox with one,
/// <see cref="SandboxScreenViewModel"/>) and a terminal are tabs. The state is read again every ten seconds while the
/// screen is open, so a change made elsewhere shows. The server does everything; this model asks and says.
/// </summary>
public sealed partial class BotSandboxViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ToastService toasts,
    TimeProvider time,
    ILogger<BotSandboxViewModel> logger) : ObservableObject, IAsyncDisposable
{
    #region Private Fields

    /// <summary>How often the state is read again, so a change made elsewhere shows.</summary>
    private static readonly TimeSpan statusEvery = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource disposal = new();

    private readonly List<SandboxCommandEntry> entries = [];

    private Guid? shownBotId;

    private bool rebuilding;

    private bool turningOff;

    private bool stoppingHold;

    private string command = string.Empty;

    private Task? watching;

    #endregion

    #region Properties

    public const string ScreenView = "screen";

    public const string TerminalView = "terminal";

    public static string LoadingNote => "Loading the sandbox.";

    public static string LoadFailedTitle => "Drover could not load this sandbox";

    public static string NotPermittedTitle => "You cannot use this sandbox";

    public static string NotPermittedCause => "Owners and admins of this space use its bots' sandboxes. You can still talk to the bot.";

    public static string MissingTitle => "This bot is not here";

    public static string MissingCause => "It was removed, or it belongs to another space.";

    public static string NoProviderTitle => "No sandbox provider is set up";

    public static string NoProviderCause =>
        "This server has no sandbox provider, so no bot has a sandbox yet. The server's operator adds a provider key; the status page says whether one is set.";

    public static string NoSandboxCause => "Give it one in the Sandbox section of its settings.";

    public static string StoppingNote => "Stopping the run. The screen frees once it stops.";

    public static string StopHoldLabel => "Stop the run that holds it";

    public static string RebuildTitle => "Rebuild the sandbox";

    public static string StopHoldTitle => "Stop the run that holds the screen";

    public static string StopHoldBody =>
        "The run that asked a person to take over this screen stops, with everything it was doing, and the bots that share this sandbox can use it again.";

    public static string TurnOffTitle => "Turn off the sandbox";

    public static string HistoryLabel => "Commands run on this visit";

    public static string CommandLabel => "Shell command";

    /// <summary>How long a shell command may be.</summary>
    public static int LongestCommand => 4000;

    public Guid BotId { get; private set; }

    public BotSummary? Bot { get; private set; }

    public BotSandboxStatus? Status { get; private set; }

    /// <summary>The tab that shows: <see cref="ScreenView"/> or <see cref="TerminalView"/>.</summary>
    public string? View { get; private set; }

    public string? LoadProblem { get; private set; }

    public string? ActionProblem { get; private set; }

    public bool NotPermitted { get; private set; }

    public bool Missing { get; private set; }

    public bool Busy { get; private set; }

    public bool Running { get; private set; }

    /// <summary>The run whose stop was asked for here, while its hold still shows.</summary>
    public Guid? StopAsked { get; private set; }

    /// <summary>Whether the dialog that asks before rebuilding is open.</summary>
    public bool Rebuilding
    {
        get => rebuilding;
        set => Set(ref rebuilding, value);
    }

    public bool TurningOff
    {
        get => turningOff;
        set => Set(ref turningOff, value);
    }

    public bool StoppingHold
    {
        get => stoppingHold;
        set => Set(ref stoppingHold, value);
    }

    public string Command
    {
        get => command;
        set => Set(ref command, value);
    }

    /// <summary>The commands of this visit, oldest first.</summary>
    public IReadOnlyList<SandboxCommandEntry> Entries => entries;

    public BotSandboxContent Shows => this switch
    {
        { LoadProblem: not null } => BotSandboxContent.LoadFailed,
        { NotPermitted: true } => BotSandboxContent.NotPermitted,
        { Missing: true } => BotSandboxContent.Missing,
        { Status: null } or { Bot: null } => BotSandboxContent.Loading,
        { Status.Available: false } => BotSandboxContent.NoProvider,
        { Status.HasSandbox: false } => BotSandboxContent.NoSandbox,
        _ => BotSandboxContent.Sandbox,
    };

    public string PageTitle => Bot is null ? "Sandbox · Drover" : $"{Bot.Name}'s sandbox · Drover";

    /// <summary>The words of the link back to the bot.</summary>
    public string BackLabel => Bot?.Name ?? "Bot";

    public string Heading => $"{Bot?.Name}'s sandbox";

    public string NoSandboxTitle => $"{Bot?.Name} has no sandbox";

    /// <summary>The state in words, for the badge. The words carry it; the badge's kind only adds color.</summary>
    public string StateWords => Status is null ? string.Empty : SandboxWords.State(Status);

    public BadgeKind StateBadge => SandboxWords.Badge(Status?.State ?? string.Empty);

    public static string BotWorkingWords => "Bot working";

    public string LastUsed => SandboxWords.LastUsed(Status?.LastUsedAt, time.GetUtcNow());

    /// <summary>A paused sandbox offers Resume where a running one offers Pause.</summary>
    public bool OffersResume => Status?.State is "paused";

    /// <summary>Who holds the screen at a bot's request, in words. Null when nobody does.</summary>
    public string? HoldWords => Status?.Hold is { } hold ? SandboxWords.Hold(hold) : null;

    /// <summary>The stop of the run that holds the screen was asked for here, and the hold still shows.</summary>
    public bool HoldIsStopping => Status?.Hold is { } hold && hold.RunId == StopAsked;

    /// <summary>Why the sandbox has no screen, in words. Null when it has one.</summary>
    public string? NoScreenNote => Status is null ? null : SandboxWords.NoScreen(Status);

    /// <summary>The bot's settings can give the sandbox a screen, so the note about a missing one links to them.</summary>
    public bool NoScreenLinksToSettings => NoScreenNote is not null && Status is { DesktopOffered: true };

    public static string SettingsLinkLabel => "Open its settings";

    public IReadOnlyList<TabItem> Views =>
        Status?.Desktop is true ? [new(ScreenView, "Screen"), new(TerminalView, "Terminal")] : [new(TerminalView, "Terminal")];

    public bool HasOne => Status?.State is "running" or "paused" or "starting";

    public bool CanPause => !Busy && Status?.State is "running";

    public bool CanReplace => !Busy && HasOne;

    public string? PowerHint => Status?.State switch
    {
        "none" => "Nothing to pause, rebuild, or turn off yet.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" when View is not ScreenView => "The sandbox is starting.",
        _ => null,
    };

    public string TerminalHint => Status switch
    {
        { State: "none" } => "Run a shell command to start the sandbox.",
        { Desktop: true } => "Run a shell command for up to five minutes. Use the interactive terminal for prompts.",
        _ => "Run a shell command for up to five minutes.",
    };

    /// <summary>The terminal offers the interactive one only where there is a desktop to open it on.</summary>
    public bool OffersInteractiveTerminal => Status is { Desktop: true } && HasOne;

    public bool CanRun => !Running && !string.IsNullOrWhiteSpace(Command);

    public string RunLabel => Running ? "Running" : "Run";

    public string RebuildBody =>
        $"{Bot?.Name} gets a fresh sandbox now. Everything in this one is lost: its files, open windows, and anything installed.";

    public string TurnOffBody =>
        $"The sandbox is removed now, and everything in it is lost: its files, open windows, and anything installed. A new, empty one starts the next time {Bot?.Name} or you use it.";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    /// <summary>
    /// Raised when the control a person just used is gone, so the focus goes to the screen's heading: the hold and
    /// its button are gone once its run stops.
    /// </summary>
    public event Action? HeadingWantsFocus;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        if (disposal.IsCancellationRequested)
        {
            return;
        }

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
                logger.LogDebug(ended, "The sandbox screen's refresh ended with a failure as it closed");
            }
        }

        disposal.Dispose();
    }

    #endregion

    #region Loading

    /// <summary>
    /// The screen was opened at a bot's sandbox, or its address changed while it shows. A link can open directly to
    /// either tab.
    /// </summary>
    public async Task Open(Guid botId, string? tab)
    {
        BotId = botId;

        if (shownBotId != botId && await session.Load() is not null)
        {
            shownBotId = botId;
            View = tab is TerminalView ? TerminalView : null;
            await Load();
        }
        else if (Status is not null)
        {
            View = tab is TerminalView || !Status.Desktop ? TerminalView : ScreenView;
        }

        Changed();
    }

    [RelayCommand]
    public async Task Load()
    {
        (Bot, Status, LoadProblem, NotPermitted, Missing) = (null, null, null, false, false);
        Changed();

        try
        {
            Bot = await api.GetBot(SpaceId, BotId);
            Status = await api.GetBotSandbox(SpaceId, BotId);
            View ??= Status.Desktop ? ScreenView : TerminalView;
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The {Screen} screen is not open to this person", nameof(BotSandboxViewModel));
            NotPermitted = true;
        }
        catch (DroverApiException absent) when (absent.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(absent, "The {Screen} screen was asked for a bot that is not there", nameof(BotSandboxViewModel));
            Missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandboxViewModel));
            LoadProblem = Failures.Sentence(exception);
        }

        Changed();
    }

    #endregion

    #region Watching

    private async Task Watch(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(statusEvery, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                await RefreshStatus(stop);
            }
        }
        catch (OperationCanceledException stopped)
        {
            logger.LogDebug(stopped, "The sandbox state stopped being read");
        }
    }

    /// <summary>Reads the state again at once, for when it has just changed.</summary>
    public Task RefreshStatus() => RefreshStatus(disposal.Token);

    private async Task RefreshStatus(CancellationToken stop)
    {
        try
        {
            Status = await api.GetBotSandbox(SpaceId, BotId, stop);
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen closed while the sandbox state was being read");
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox state could not be read again");
        }
        catch (Exception unexpected)
        {
            // Whatever goes wrong with one read, the next one still comes: only leaving the screen ends the reads.
            logger.LogWarning(unexpected, "Reading the sandbox state again failed unexpectedly; the next read still comes");
        }

        Changed();
    }

    #endregion

    #region Power

    [RelayCommand]
    public Task Pause() => Power(() => api.PauseBotSandbox(SpaceId, BotId), "The sandbox is paused. Its files are kept.");

    [RelayCommand]
    public Task Resume() => Power(() => api.ResumeBotSandbox(SpaceId, BotId), "The sandbox is running again.");

    [RelayCommand]
    public async Task Rebuild()
    {
        rebuilding = false;
        await Power(() => api.RebuildBotSandbox(SpaceId, BotId), "The sandbox was rebuilt. It is a fresh one.");
    }

    [RelayCommand]
    public async Task TurnOff()
    {
        turningOff = false;
        await Power(() => api.TurnOffBotSandbox(SpaceId, BotId), "The sandbox is turned off. A new one starts on its next use.");
    }

    // An owner or admin may stop any run; the hold ends with it.
    [RelayCommand]
    public async Task StopHold()
    {
        stoppingHold = false;

        if (Status?.Hold is not { } hold)
        {
            Changed();
            return;
        }

        // Stopping is asked for: a run that waits in another person's conversation stops when the server gets to it,
        // so while the hold still shows the screen says it is stopping, until its ten-second state read shows it gone.
        Busy = true;
        ActionProblem = null;
        Changed();

        try
        {
            await api.CancelRun(SpaceId, hold.RunId);
            StopAsked = hold.RunId;
            Status = await api.GetBotSandbox(SpaceId, BotId);

            if (Status.Hold is null)
            {
                toasts.Show("The run stopped. The screen is free.");
            }

            // The button the person used is gone either way: replaced by the stopping line, or by nothing.
            Busy = false;
            Changed();
            HeadingWantsFocus?.Invoke();
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandboxViewModel));
            ActionProblem = Failures.Sentence(exception, "Check that the server is running.");
        }

        Busy = false;
        Changed();
    }

    private async Task Power(Func<Task<BotSandboxStatus>> change, string outcome)
    {
        Busy = true;
        ActionProblem = null;
        Changed();

        try
        {
            Status = await change();
            toasts.Show(outcome);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandboxViewModel));
            ActionProblem = Failures.Sentence(exception, "Check that the server is running.");
        }

        Busy = false;
        Changed();
    }

    #endregion

    #region Terminal

    /// <summary>Shows a tab and puts it in the address, so the address opens to the same tab.</summary>
    public void SelectView(string selected)
    {
        if (View == selected)
        {
            return;
        }

        View = selected;
        Changed();
        navigator.NavigateTo($"{Addresses.BotSandbox(BotId)}?tab={selected}", replace: true);
    }

    [RelayCommand]
    public async Task OpenTerminal()
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        ActionProblem = null;
        Changed();
        try
        {
            await api.SendBotSandboxInput(SpaceId, BotId, new SandboxInputRequest("open_terminal"), disposal.Token);
            SelectView(ScreenView);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox desktop terminal could not open");
            ActionProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
        }

        await RefreshStatus(disposal.Token);
    }

    public async Task RunCommand()
    {
        if (Running || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var entry = new SandboxCommandEntry(command);
        entries.Add(entry);
        command = string.Empty;
        Running = true;
        Changed();

        try
        {
            entry.Result = await api.RunBotSandboxCommand(SpaceId, BotId, entry.Command);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "A command in the sandbox was refused");
            entry.Problem = Failures.Sentence(exception, "Check that the server is running.");
        }

        Running = false;
        await RefreshStatus(disposal.Token);
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

    /// <summary>
    /// Whatever shows this model reads all of it again: one change can alter many of its sentences. The state starts
    /// being read on its timer once there is a sandbox to read.
    /// </summary>
    private void Changed()
    {
        if (Status is { Available: true, HasSandbox: true } && watching is null && !disposal.IsCancellationRequested)
        {
            watching = Watch(disposal.Token);
        }

        OnPropertyChanged(string.Empty);
    }

    #endregion
}
