namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What the screen panel has to show under a request the person holds.</summary>
public enum ScreenPanelState
{
    Loading,
    Failed,
    NotPermitted,
    NoProvider,
    NoScreenButTerminal,
    NoScreen,
    NoSandbox,
    Screen,
}


/// <summary>
/// A bot's sandbox screen beside its conversation. Its job: let a person watch what the bot does on its screen while
/// they talk to it, and take control of the mouse and keyboard when they choose. The sandbox's state is read when the
/// panel opens and every ten seconds while it is open, so the bot's work shows. Power and the terminal stay on the
/// sandbox screen, which the panel links to.
/// </summary>
public sealed class ScreenPanelViewModel(
    DroverApi api,
    SessionState session,
    TimeProvider time,
    ILogger<ScreenPanelViewModel> logger) : ObservableObject, IAsyncDisposable
{
    #region Private Fields

    private static readonly TimeSpan statusEvery = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource disposal = new();

    private bool notPermitted;

    private Task? watching;

    #endregion

    #region Properties

    /// <summary>The bot whose screen is shown.</summary>
    public BotSummary Bot { get; set; } = null!;

    /// <summary>The bot's request for the person at this screen, while the person holds it. Its controls are shown here.</summary>
    public Takeover? Takeover { get; set; }

    /// <summary>The sandbox as last read. The screen's surface draws from it.</summary>
    public BotSandboxStatus? Status { get; private set; }

    /// <summary>Why the screen could not be loaded, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>True while the answer to the held request is on its way.</summary>
    public bool Handing { get; private set; }

    /// <summary>Why the answer could not be sent, as a sentence.</summary>
    public string? HandProblem { get; private set; }

    /// <summary>The request whose controls stand above the screen, while the bot waits for it.</summary>
    public Takeover? Held => Takeover is { Waits: true } asked ? asked : null;

    /// <summary>True while the person holds the screen for the bot's request: the surface says so to the person.</summary>
    public bool IsHeld => Held is not null;

    public ScreenPanelState State => (Problem, notPermitted, Status) switch
    {
        (not null, _, _) => ScreenPanelState.Failed,
        (_, true, _) => ScreenPanelState.NotPermitted,
        (_, _, null) => ScreenPanelState.Loading,
        (_, _, { Available: false }) => ScreenPanelState.NoProvider,
        (_, _, { DesktopOffered: false, HasSandbox: true }) => ScreenPanelState.NoScreenButTerminal,
        (_, _, { DesktopOffered: false }) => ScreenPanelState.NoScreen,
        (_, _, { HasSandbox: false }) => ScreenPanelState.NoSandbox,
        (_, _, { Desktop: false }) => ScreenPanelState.NoScreen,
        _ => ScreenPanelState.Screen,
    };

    public string NotPermittedHint => $"Owners and admins of this space watch and use its bots' screens. You can still talk to {Bot.Name}.";

    /// <summary>The title of the state that has nothing to show, where there is one.</summary>
    public string? EmptyTitle => State switch
    {
        ScreenPanelState.Failed => FailedTitle,
        ScreenPanelState.NoProvider => NoProviderTitle,
        ScreenPanelState.NoScreenButTerminal or ScreenPanelState.NoScreen => $"{Bot.Name} has no screen",
        ScreenPanelState.NoSandbox => $"{Bot.Name} has no sandbox",
        _ => null,
    };

    public string? EmptyCause => State switch
    {
        ScreenPanelState.Failed => Problem,
        ScreenPanelState.NoProvider => NoProviderCause,
        ScreenPanelState.NoScreenButTerminal or ScreenPanelState.NoScreen => SandboxWords.NoScreen(Status!),
        ScreenPanelState.NoSandbox => NoSandboxCause,
        _ => null,
    };

    public string? EmptyAction => State switch
    {
        ScreenPanelState.Failed => TryAgain,
        ScreenPanelState.NoProvider => "Open the status page",
        ScreenPanelState.NoScreenButTerminal => "Open its terminal",
        ScreenPanelState.NoScreen or ScreenPanelState.NoSandbox => "Open its settings",
        _ => null,
    };

    /// <summary>Where the state's one next step is. Null where the step is trying again.</summary>
    public string? EmptyHref => State switch
    {
        ScreenPanelState.NoProvider => Addresses.Status,
        ScreenPanelState.NoScreenButTerminal => Addresses.BotSandbox(Bot.Id),
        ScreenPanelState.NoScreen or ScreenPanelState.NoSandbox => Addresses.Bot(Bot.Id),
        _ => null,
    };

    public string SandboxState => SandboxWords.State(Status!);

    public BadgeKind SandboxKind => SandboxWords.Badge(Status!.State);

    public bool BotWorking => Status?.BotWorking ?? false;

    public string TerminalHref => Addresses.BotTerminal(Bot.Id);

    /// <summary>The person handed the screen back, or found it already answered.</summary>
    public event Action<Guid>? HandedBack;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

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
                logger.LogDebug(ended, "The screen panel's refresh ended with a failure as it closed");
            }
        }

        disposal.Dispose();
    }

    #endregion

    #region Public Methods

    /// <summary>Reads the sandbox's state, then keeps reading it until the panel closes.</summary>
    public async Task Open()
    {
        // Asked once, before the first await: the panel can close while its first read is on its way.
        var stop = disposal.LeavingToken();
        await Load();

        if (!stop.IsCancellationRequested)
        {
            watching = Watch(stop);
        }
    }

    // The answer is the call's result. Once it is stored, or was stored already, the request no longer waits here.
    public async Task HandBack(bool done, string? note)
    {
        if (Handing || Takeover is not { } asked)
        {
            return;
        }

        Handing = true;
        Changed();
        HandProblem = await api.HandBack(SpaceId, asked.ToolCallId, done, note, logger);
        Handing = false;

        if (HandProblem is null or TakeoverAnswers.AnsweredAlready)
        {
            HandProblem = null;
            HandedBack?.Invoke(asked.ToolCallId);
        }

        Changed();
    }

    public async Task Load()
    {
        Problem = null;

        try
        {
            Status = await api.GetBotSandbox(SpaceId, Bot.Id, disposal.LeavingToken());
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The screen of bot {BotId} is not open to this person", Bot.Id);
            notPermitted = true;
        }
        catch (OperationCanceledException stopped) when (disposal.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel closed while the sandbox state was being read");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ScreenPanelViewModel));
            Problem = Failures.Sentence(exception);
        }

        Changed();
    }

    /// <summary>Reads the sandbox's state again now, as the screen's surface asks when it changed the sandbox.</summary>
    public Task Refresh() => Refresh(disposal.LeavingToken());

    #endregion

    #region Fixed Words

    public const string FailedTitle = "Drover could not load the screen";

    public const string TryAgain = "Try again";

    public const string Loading = "Loading the screen.";

    public const string NoProviderTitle = "No sandbox provider is set up";

    public const string NoProviderCause = "This server has no sandbox provider, so no bot has a screen yet.";

    public const string NoSandboxCause = "Give it a sandbox with Desktop on, in the Sandbox section of its settings.";

    public const string BotWorkingWords = "Bot working";

    public const string TerminalText = "Open terminal and power controls";

    #endregion

    #region Private Methods

    private async Task Watch(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(statusEvery, time);

        try
        {
            while (!notPermitted && await timer.WaitForNextTickAsync(stop))
            {
                await Refresh(stop);
            }
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel stopped reading the sandbox state");
        }
    }

    // A failure to read the state again leaves the screen as it was; the picture says for itself when it cannot be read.
    // A read that works clears a first load that failed, so the screen recovers without a tap. Whatever goes wrong with
    // one read, the next one still comes: only closing the screen ends the reads.
    private async Task Refresh(CancellationToken stop)
    {
        // A panel that was closed has nothing to show a state to.
        if (stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            Status = await api.GetBotSandbox(SpaceId, Bot.Id, stop);
            Problem = null;
            Changed();
        }
        catch (OperationCanceledException stopped) when (stop.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "The screen panel closed while the sandbox state was being read again");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox state could not be read again in the {Screen}", nameof(ScreenPanelViewModel));
        }
        catch (Exception unexpected)
        {
            logger.LogWarning(unexpected, "Reading the sandbox state again failed unexpectedly in the {Screen}; the next read still comes", nameof(ScreenPanelViewModel));
        }
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
