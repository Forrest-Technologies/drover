namespace Drover.Ui.Pages.Bots;

/// <summary>
/// A bot's sandbox. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart
/// it. The state and the power controls sit at the top; the screen (for a sandbox with one, <see cref="SandboxScreen"/>)
/// and a terminal are tabs. The state is read again every ten seconds while the page is open, so a change made
/// elsewhere shows. The server does everything; this page asks and shows.
/// </summary>
public sealed partial class BotSandbox(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    TimeProvider time,
    ILogger<BotSandbox> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string ScreenView = "screen";

    private const string TerminalView = "terminal";

    /// <summary>How often the state is read again, so a change made elsewhere shows.</summary>
    private static readonly TimeSpan statusEvery = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource disposal = new();

    private readonly List<CommandEntry> entries = [];

    private BotSummary? bot;

    private BotSandboxStatus? status;

    private Guid? shownBotId;

    private string? view;

    private string? loadProblem;

    private string? actionProblem;

    private bool notPermitted;

    private bool missing;

    private bool busy;

    private bool running;

    private bool rebuilding;

    private bool turningOff;

    private bool stoppingHold;

    // The run whose stop was asked for here, while its hold still shows.
    private Guid? stopAsked;

    // The hold and its button are gone once its run stops, so the focus goes to the page's heading.
    private bool focusHeading;

    private ElementReference heading;

    private string command = string.Empty;

    private Task? watching;

    #endregion

    #region Properties

    [Parameter]
    public Guid BotId { get; set; }

    /// <summary>A link can open directly to the terminal.</summary>
    [SupplyParameterFromQuery(Name = "tab")]
    public string? Tab { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private IReadOnlyList<TabItem> Views =>
        status?.Desktop is true ? [new(ScreenView, "Screen"), new(TerminalView, "Terminal")] : [new(TerminalView, "Terminal")];

    private bool HasOne => status?.State is "running" or "paused" or "starting";

    private string? PowerHint => status?.State switch
    {
        "none" => "Nothing to pause, rebuild, or turn off yet.",
        "replacing" => "The old sandbox is being taken down. A new one starts on its next use.",
        "starting" when view is not ScreenView => "The sandbox is starting.",
        _ => null,
    };

    private string TerminalHint => status switch
    {
        { State: "none" } => "Run a shell command to start the sandbox.",
        { Desktop: true } => "Run a shell command for up to five minutes. Use the interactive terminal for prompts.",
        _ => "Run a shell command for up to five minutes.",
    };

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
                logger.LogDebug(ended, "The sandbox screen's refresh ended with a failure as it closed");
            }
        }

        disposal.Dispose();
    }

    #endregion

    #region Overrides

    protected override async Task OnParametersSetAsync()
    {
        if (shownBotId != BotId && await session.Load() is not null)
        {
            shownBotId = BotId;
            view = Tab is TerminalView ? TerminalView : null;
            await Load();
        }
        else if (Tab is TerminalView)
        {
            view = TerminalView;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (status is { Available: true, HasSandbox: true } && watching is null)
        {
            watching = Watch(disposal.Token);
        }

        if (focusHeading)
        {
            focusHeading = false;
            await heading.FocusAsync();
        }
    }

    #endregion

    #region Loading

    private async Task Load()
    {
        (bot, status, loadProblem, notPermitted, missing) = (null, null, null, false, false);

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
        catch (Exception unexpected)
        {
            // Whatever goes wrong with one read, the next one still comes: only leaving the page ends the reads.
            logger.LogWarning(unexpected, "Reading the sandbox state again failed unexpectedly; the next read still comes");
        }

        await InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Power

    private Task Pause() => Power(() => api.PauseBotSandbox(SpaceId, BotId), "The sandbox is paused. Its files are kept.");

    private Task Resume() => Power(() => api.ResumeBotSandbox(SpaceId, BotId), "The sandbox is running again.");

    private async Task Rebuild()
    {
        rebuilding = false;
        await Power(() => api.RebuildBotSandbox(SpaceId, BotId), "The sandbox was rebuilt. It is a fresh one.");
    }

    private async Task TurnOff()
    {
        turningOff = false;
        await Power(() => api.TurnOffBotSandbox(SpaceId, BotId), "The sandbox is turned off. A new one starts on its next use.");
    }

    // An owner or admin may stop any run; the hold ends with it.
    private async Task StopHold()
    {
        stoppingHold = false;

        if (status?.Hold is not { } hold)
        {
            return;
        }

        // Stopping is asked for: a run that waits in another person's conversation stops when the server gets to it,
        // so while the hold still shows the page says it is stopping, until its ten-second state read shows it gone.
        busy = true;
        actionProblem = null;

        try
        {
            await api.CancelRun(SpaceId, hold.RunId);
            stopAsked = hold.RunId;
            status = await api.GetBotSandbox(SpaceId, BotId);

            if (status.Hold is null)
            {
                toasts.Show("The run stopped. The screen is free.");
            }

            // The button the person used is gone either way: replaced by the stopping line, or by nothing.
            focusHeading = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotSandbox));
            actionProblem = Failures.Sentence(exception);
        }

        busy = false;
    }

    private async Task Power(Func<Task<BotSandboxStatus>> change, string outcome)
    {
        busy = true;
        actionProblem = null;

        try
        {
            status = await change();
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

    #region Terminal

    private async Task OpenTerminal()
    {
        if (busy)
        {
            return;
        }

        busy = true;
        actionProblem = null;
        try
        {
            await api.SendBotSandboxInput(SpaceId, BotId, new SandboxInputRequest("open_terminal"), disposal.Token);
            view = ScreenView;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox desktop terminal could not open");
            actionProblem = Failures.Sentence(exception);
        }
        finally
        {
            busy = false;
        }

        await RefreshStatus(disposal.Token);
    }

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
