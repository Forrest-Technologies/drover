namespace Drover.Ui.Pages;

/// <summary>
/// The workspace: the person picks a bot from the crew and talks to it in its main conversation. A side chat with
/// the same bot opens beside it.
/// </summary>
public sealed partial class Talk(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ILogger<Talk> logger) : IDisposable
{
    #region Private Fields

    private const string NewSideChatName = "New side chat";

    private const string SideRail = "side";

    private const string SummaryRail = "summary";

    private const string ScreenRail = "screen";

    private const string FilesRail = "files";

    private const string MemorySide = "memory";

    private const string MomentPrefix = "#message-";

    private const string NewSideChat = "new";

    private readonly Dictionary<Guid, ThreadSummary> threads = [];

    private IReadOnlyList<BotSummary>? bots;

    private IReadOnlyList<ModelConnectionSummary>? connections;

    private string? spaceProblem;

    private bool formerMissing;

    private bool mainBusy;

    // Work the conversations on screen have handed to other bots, which the crew column draws on its connectors.
    private IReadOnlyDictionary<Guid, Handoff> mainHandoffs = ImmutableDictionary<Guid, Handoff>.Empty;

    private IReadOnlyDictionary<Guid, Handoff> sideHandoffs = ImmutableDictionary<Guid, Handoff>.Empty;

    // The side pane is kept while a new side chat takes the address of the thread its first message made.
    private object sideKey = new();

    private string? shownSide;

    private Guid? startedThreadId;

    // The bot whose sandbox was last asked whether it has a screen, and whether it does and this person may see it.
    private Guid? screenAskedFor;

    private Guid? screenKnownFor;

    private ScreenAccess screenAccess;

    private bool hasScreenLink;

    private string? screenSetupHref;

    private bool focusRailClose;

    private ElementReference railClose;

    // A request for the person at the bot's screen: the one waiting in the main conversation, the one the person took
    // over (from a side chat too, which the screen replaces), and the one they handed back while its events were on
    // their way. The screen beside the conversation shows the controls for whichever is held.
    private Takeover? mainTakeover;

    private Takeover? openedTakeover;

    private Guid? handedBack;

    // Raised each time the bot stops working in the main conversation, so the summary on screen is read again.
    private int summaryVersion;

    /// <summary>Changes each time a conversation with the shown bot has moved on, so the files rail reads its list again.</summary>
    private int conversationVersion;

    // Follows the person's signals while the page is open, so what each bot has for them changes without a reload.
    private CancellationTokenSource? listening;

    #endregion

    #region Properties

    /// <summary>The bot being talked to. Without one, the crew's lead is.</summary>
    [Parameter]
    public Guid? BotId { get; set; }

    /// <summary>
    /// What is open beside the main conversation: "summary" for its summary, "memory" for what the bot remembers about
    /// the person, "screen" for the bot's screen, "files" for the files exchanged with it, "side" for its side chats.
    /// </summary>
    [Parameter]
    public string? Rail { get; set; }

    /// <summary>The side chat that is open: its thread, or "new" for one its first message will make.</summary>
    [Parameter]
    public string? SideChat { get; set; }

    /// <summary>The bot whose side of handed work is open beside the conversation.</summary>
    [Parameter]
    public Guid? WorkBotId { get; set; }

    /// <summary>That bot's thread for the work.</summary>
    [Parameter]
    public Guid? WorkThreadId { get; set; }

    /// <summary>A thread opened by the address it had before conversations were reached through their bot.</summary>
    [Parameter]
    public Guid? FormerThreadId { get; set; }

    /// <summary>Set when the browser comes back from a tool server's sign-in page: "connected" or "failed".</summary>
    [SupplyParameterFromQuery(Name = "tool-sign-in")]
    public string? ToolSignIn { get; set; }

    /// <summary>The tool connection that sign-in was for.</summary>
    [SupplyParameterFromQuery(Name = "connection")]
    public Guid? ToolSignInConnection { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private Guid? ThreadId => Guid.TryParse(SideChat, out var id) ? id : null;

    // Read from the address's own parts, so the rail and the address never disagree. A side chat that is neither
    // new nor a thread falls back to the list.
    private SideView Side => (Rail, SideChat, ThreadId) switch
    {
        _ when WorkThreadId is not null => SideView.Work,
        (_, _, not null) => SideView.Chat,
        (_, NewSideChat, _) => SideView.New,
        (_, not null, _) or (SideRail, _, _) => SideView.List,
        (SummaryRail, _, _) => SideView.Summary,
        (ScreenRail, _, _) => SideView.Screen,
        (FilesRail, _, _) => SideView.Files,
        (MemorySide, _, _) => SideView.Memory,
        _ => SideView.Closed,
    };

    // A phone shows one of the crew, the main conversation, and the side chats. The start page is the crew.
    private string At => (BotId, FormerThreadId, Side) switch
    {
        (null, null, _) => "crew",
        (_, _, SideView.Closed) => "main",
        _ => "side",
    };

    // Only a request the person took over here is held: opening the screen another way does not answer anything.
    private Takeover? HeldTakeover => openedTakeover is { } held && held.ToolCallId != handedBack ? held : null;

    private Guid? HeldCallId => Side is SideView.Screen ? HeldTakeover?.ToolCallId : null;

    // The message a link to a moment names, from the address's fragment.
    private Guid? Moment
    {
        get
        {
            var fragment = new Uri(navigation.Uri).Fragment;
            return fragment.StartsWith(MomentPrefix, StringComparison.Ordinal) && Guid.TryParse(fragment[MomentPrefix.Length..], out var id) ? id : null;
        }
    }

    private IReadOnlyList<CrewNode> Crew => CrewTree.From(bots!);

    // Asked for before the space has loaded too: the page's title is drawn outside the session gate.
    private BotSummary? Shown => (bots, BotId) switch
    {
        (null, _) => null,
        (_, { } id) => bots.FirstOrDefault(bot => bot.Id == id),
        _ => CrewTree.Lead(bots),
    };

    // A bot working on work handed to it is working for this person too.
    private IReadOnlySet<Guid> WorkingBots =>
        threads.Values
            .Where(thread => thread.ActiveRunId is not null && thread.Attention?.WaitingForYou is not true)
            .Select(thread => thread.BotId)
            .Concat(mainBusy && Shown is not null ? [Shown.Id] : [])
            .Concat(Handoffs.Where(pair => pair.Value is Handoff.Working).Select(pair => pair.Key))
            .ToHashSet();

    // Work out wins over work that came back, so a bot both conversations handed work to shows it out. A side chat's
    // work counts while the side chat is open.
    private IReadOnlyDictionary<Guid, Handoff> Handoffs =>
        mainHandoffs.Concat(Side is SideView.Chat or SideView.New ? sideHandoffs : [])
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Value));

    private BotSummary? WorkBot => bots?.FirstOrDefault(bot => bot.Id == WorkBotId);

    private string WorkName => WorkBot?.Name ?? Handover.Unknown;

    private Func<Guid, Guid, string> WorkAddress => (worker, thread) => Addresses.Work(Shown!.Id, worker, thread);

    // What each bot has for this person, across all their threads with it. A conversation on screen is being seen,
    // so it has nothing new and no failure to point at, even before the server has heard so.
    private IReadOnlyDictionary<Guid, Attention> ForYou =>
        AttentionWords.OfCrew(threads.Values.Select(thread => IsOnScreen(thread) && thread.Attention is { } attention
            ? thread with { Attention = attention with { Failed = false, HasNew = false } }
            : thread));

    private IReadOnlyList<SideChatItem> SideChats =>
    [
        .. threads.Values
            .Where(thread => thread.BotId == Shown!.Id && !thread.IsMain)
            .OrderByDescending(thread => thread.LastActivityAt)
            .Select(thread => new SideChatItem(
                thread.Id,
                thread.Title ?? NewSideChatName,
                Addresses.SideChat(thread.BotId, thread.Id),
                Shown!.Name,
                BotIdentity.Hue(thread.BotId),
                thread.ActiveRunId is not null,
                AttentionWords.OfCrew([thread]).GetValueOrDefault(thread.BotId))),
    ];

    private string SideChatsLabel => SideChats.Count switch
    {
        0 => "Side chats",
        1 => "1 side chat",
        var count => $"{count} side chats",
    };

    private string SideTitle => Side switch
    {
        SideView.Chat => threads.GetValueOrDefault(ThreadId!.Value)?.Title ?? "Side chat",
        SideView.Work => $"{WorkName}'s side",
        SideView.New => NewSideChatName,
        SideView.Summary => "Summary",
        SideView.Screen => "Screen",
        SideView.Files => "Files",
        SideView.Memory => "Memory",
        _ => "Side chats",
    };

    private string RailName => Side switch
    {
        SideView.Summary => $"Summary of the conversation with {Shown!.Name}",
        SideView.Screen => $"{Shown!.Name}'s screen",
        SideView.Work => $"{WorkName}'s side of work handed to it",
        SideView.Files => $"Files exchanged with {Shown!.Name}",
        SideView.Memory => $"What {Shown!.Name} remembers about you",
        _ => $"Side chats with {Shown!.Name}",
    };

    private string CloseRailName => Side switch
    {
        SideView.Summary => "Close the summary",
        SideView.Screen => "Close the screen",
        SideView.Work => $"Close {WorkName}'s side",
        SideView.Files => "Close the files",
        SideView.Memory => "Close memory",
        _ => "Close side chats",
    };

    // The tab says how many bots wait for the person, so it can be seen from another tab.
    private string PageName => AttentionWords.TabTitle(
        (bots, Shown, Side) switch
        {
            (null, _, _) or (_, null, _) => "Drover",
            (_, { } bot, SideView.Closed) => $"{bot.Name} · Drover",
            (_, { } bot, _) => $"{SideTitle} · {bot.Name} · Drover",
        },
        AttentionWords.Waiting(ForYou));

    #endregion

    #region Public Methods

    public void Dispose()
    {
        listening?.Cancel();
        listening?.Dispose();
    }

    #endregion

    #region Loading

    protected override async Task OnParametersSetAsync()
    {
        if (await session.Load() is null)
        {
            return;
        }

        // A sign-in at a tool server ends here, at the start page. The tool connections screen says how it went.
        if (ToolSignIn is "connected" or "failed")
        {
            navigation.NavigateTo(Addresses.ToolSignIn(ToolSignIn, ToolSignInConnection?.ToString()), replace: true);
            return;
        }

        if (bots is null && spaceProblem is null)
        {
            await LoadSpace();
        }

        if (FormerThreadId is { } former && bots is { Count: > 0 })
        {
            await FollowFormerAddress(former);
        }

        KeepOrRenewSidePane();
        await AskForScreen();
    }

    private async Task LoadSpace()
    {
        spaceProblem = null;
        try
        {
            connections = await api.ListModelConnections(SpaceId);
            var found = await api.ListBots(SpaceId);
            await LoadThreads(found, CancellationToken.None);
            bots = found;
            Listen();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Talk));
            spaceProblem = Failures.Sentence(exception);
        }
    }

    // Each thread arrives with what it has for this person, so the crew's first paint already says it.
    private async Task LoadThreads(IReadOnlyList<BotSummary> crew, CancellationToken cancellationToken)
    {
        Dictionary<Guid, ThreadSummary> found = [];
        foreach (var bot in crew)
        {
            foreach (var thread in await api.ListThreads(SpaceId, bot.Id, cancellationToken))
            {
                found[thread.Id] = thread;
            }
        }

        threads.Clear();
        foreach (var (id, thread) in found)
        {
            threads[id] = thread;
        }
    }

    // A link kept from before goes to where that conversation is now: the bot's main conversation or one of its
    // side chats.
    private async Task FollowFormerAddress(Guid threadId)
    {
        formerMissing = false;
        try
        {
            var thread = threads.GetValueOrDefault(threadId) ?? await api.GetThread(SpaceId, threadId);
            navigation.NavigateTo(thread.IsMain ? Addresses.Talk(thread.BotId) : Addresses.SideChat(thread.BotId, thread.Id), replace: true);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", threadId);
            formerMissing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Talk));
            spaceProblem = Failures.Sentence(exception);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusRailClose)
        {
            focusRailClose = false;
            await railClose.FocusAsync();
        }
    }

    #endregion

    #region Following Signals

    private bool IsOnScreen(ThreadSummary thread) =>
        thread.Id == ThreadId || (thread.IsMain && thread.BotId == Shown?.Id && FormerThreadId is null && At is not "crew");

    private void Listen()
    {
        if (listening is null)
        {
            listening = new();
            _ = FollowSignals(listening.Token);
        }
    }

    // Each signal names a thread that changed for this person, on this screen or another: it is read again, and the
    // crew and the tab's title follow. A dropped connection may have missed some, so then every thread is read again.
    private async Task FollowSignals(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var signal in api.WatchSignals(SpaceId, cancellationToken: cancellationToken))
            {
                await Follow(signal, cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped following signals on the {Screen} screen", nameof(Talk));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // Refused, not dropped: connecting again would not change it. The crew still changes as this screen works.
            logger.LogWarning(exception, "The signal stream was refused on the {Screen} screen", nameof(Talk));
        }
    }

    private async Task Follow(UserSignal signal, CancellationToken cancellationToken)
    {
        try
        {
            if (signal.Kind is UserSignalKinds.Reconnected)
            {
                var crew = await api.ListBots(SpaceId, cancellationToken);
                await LoadThreads(crew, cancellationToken);
                bots = crew;
            }
            else if (signal.ThreadId is { } threadId)
            {
                threads[threadId] = await api.GetThread(SpaceId, threadId, cancellationToken);
            }
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound && signal.ThreadId is { } gone)
        {
            logger.LogInformation(exception, "Thread {ThreadId} is no longer visible on the {Screen} screen", gone, nameof(Talk));
            threads.Remove(gone);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // The crew keeps what it showed; the next signal or reconnection reads it again.
            logger.LogWarning(exception, "A thread could not be read again on the {Screen} screen", nameof(Talk));
        }
    }

    #endregion

    #region Private Methods

    private void ScreenTakenOver(Takeover takeover)
    {
        (openedTakeover, handedBack) = (takeover, null);

        if (Shown is { } bot)
        {
            navigation.NavigateTo(Addresses.Screen(bot.Id));
        }
    }

    private void MainTakeoverChanged(Takeover? takeover)
    {
        if (takeover is null && openedTakeover?.ToolCallId == mainTakeover?.ToolCallId)
        {
            openedTakeover = null;
        }

        mainTakeover = takeover;
    }

    // The controls the person used are gone, so the focus goes to the screen's close control, the one way on.
    private void HandedBack(Guid toolCallId)
    {
        handedBack = toolCallId;
        openedTakeover = null;
        focusRailClose = true;
    }

    // The way to the screen is offered only for a bot whose sandbox has one that this person may watch. A person who
    // may not, or a failure to ask, leaves it out; the conversation does not need it.
    private async Task AskForScreen()
    {
        if (Shown is not { } bot || screenAskedFor == bot.Id)
        {
            return;
        }

        screenAskedFor = bot.Id;

        // Asked again for the same bot, the last answer stands until the new one comes.
        if (screenKnownFor != bot.Id)
        {
            screenAccess = ScreenAccess.Unknown;
        }

        try
        {
            // A sandbox that is stopped or not made yet still has a screen: the screen itself offers to start it.
            var sandbox = await api.GetBotSandbox(SpaceId, bot.Id);
            screenAccess = sandbox is { Available: true, HasSandbox: true, Desktop: true } ? ScreenAccess.Usable : ScreenAccess.Missing;
            screenSetupHref = sandbox.Available ? Addresses.Bot(bot.Id) : Addresses.Status;
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "This person does not use bot {BotId}'s screen", bot.Id);
            screenAccess = ScreenAccess.NotPermitted;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // Not known: a request to take the screen over still offers it, and the screen says what failed.
            logger.LogInformation(exception, "Whether bot {BotId} has a screen is not known on the {Screen} screen", bot.Id, nameof(Talk));
            screenAccess = screenAccess is ScreenAccess.Unknown ? ScreenAccess.Usable : screenAccess;
            hasScreenLink = false;
            screenKnownFor = bot.Id;
            return;
        }

        hasScreenLink = screenAccess is ScreenAccess.Usable;
        screenKnownFor = bot.Id;
    }

    // A different side chat gets a pane of its own. The one exception is a new side chat whose first message has
    // just made its thread: the address changes and the conversation on screen stays as it is.
    private void KeepOrRenewSidePane()
    {
        var side = Side switch
        {
            SideView.Chat => ThreadId.ToString(),
            SideView.New => NewSideChat,
            _ => null,
        };

        if (side != shownSide && !(shownSide is NewSideChat && ThreadId is not null && ThreadId == startedThreadId))
        {
            sideKey = new();
        }

        shownSide = side;
    }

    private void SideChatStarted(ThreadSummary started)
    {
        startedThreadId = started.Id;
        threads[started.Id] = started;
        navigation.NavigateTo(Addresses.SideChat(started.BotId, started.Id), replace: true);
    }

    private void Remember(ThreadSummary changed) => threads[changed.Id] = changed;

    private async Task MainBusyChanged(bool busy)
    {
        mainBusy = busy;
        if (!busy)
        {
            summaryVersion++;
            conversationVersion++;
            await RefreshCrew();
        }

        // A bot may have been given a screen, or started its sandbox, while it worked.
        screenAskedFor = null;
        await AskForScreen();
    }

    private void MainHandoffsChanged(IReadOnlyDictionary<Guid, Handoff> handoffs) => mainHandoffs = handoffs;

    private void SideHandoffsChanged(IReadOnlyDictionary<Guid, Handoff> handoffs) => sideHandoffs = handoffs;

    private Task SideBusyChanged(bool busy)
    {
        if (busy)
        {
            return Task.CompletedTask;
        }

        conversationVersion++;
        return RefreshCrew();
    }

    // A bot may have made, changed, or removed bots while it worked. The crew is read again when it stops; if that
    // fails the crew on screen stays as it was, and the next time shows it.
    private async Task RefreshCrew()
    {
        try
        {
            bots = await api.ListBots(SpaceId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The crew could not be read again on the {Screen} screen", nameof(Talk));
        }
    }

    #endregion
}
