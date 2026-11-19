namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The workspace: the person picks a bot from the crew and talks to it in its main conversation. A side chat with
/// the same bot opens beside it. Everything it shows is read from the address it was opened at, so the screen and
/// the address never disagree; the conversations on screen are kept while the address moves within this screen.
/// </summary>
public sealed class TalkViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ToastService toasts,
    ShareInbox inbox,
    IUiThread ui,
    ILoggerFactory loggers) : ObservableObject, IDisposable
{
    #region Private Fields

    private const string NewSideChatName = "New side chat";

    private const string SideRail = "side";

    private const string SummaryRail = "summary";

    private const string ScreenRail = "screen";

    private const string ActivityRail = "activity";

    private const string FilesRail = "files";

    private const string MemorySide = "memory";

    private const string MomentPrefix = "message-";

    private const string NewSideChat = "new";

    private const string AtCrew = "crew";

    private const string AtMain = "main";

    private const string AtSide = "side";

    private readonly ILogger<TalkViewModel> logger = loggers.CreateLogger<TalkViewModel>();

    private readonly Dictionary<Guid, ThreadSummary> threads = [];

    private TalkAddress address = new();

    private IReadOnlyList<BotSummary>? bots;

    private IReadOnlyList<ModelConnectionSummary>? connections;

    private string? spaceProblem;

    private bool formerMissing;

    private bool mainBusy;

    // Work the conversations on screen have handed to other bots, which the crew column draws on its connectors.
    private IReadOnlyDictionary<Guid, Handoff> mainHandoffs = ImmutableDictionary<Guid, Handoff>.Empty;

    private IReadOnlyDictionary<Guid, Handoff> sideHandoffs = ImmutableDictionary<Guid, Handoff>.Empty;

    // The side conversation is kept while a new side chat takes the address of the thread its first message made.
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

    // The rail was closed from the keyboard, so the focus goes back to the way that opened it once it is gone.
    private bool focusRailOpener;

    private SideView? railOpener;

    // A request for the person at the bot's screen: the one waiting in the main conversation, the one the person took
    // over (from a side chat too, which the screen replaces), and the one they handed back while its events were on
    // their way. The screen beside the conversation shows the controls for whichever is held.
    private Takeover? mainTakeover;

    private Takeover? openedTakeover;

    private Guid? handedBack;

    // Follows the person's signals while the screen is open, so what each bot has for them changes without a reload.
    private CancellationTokenSource? listening;

    // The conversations on screen, each with what it was made for: a different bot, side chat, or thread gets a new one.
    private object? mainPaneKey;

    private object? sidePaneKey;

    private object? workPaneKey;

    private bool gone;

    // A conversation that is told something may tell this screen something back at once. That is taken in after
    // the round under way, never in the middle of it.
    private bool showing;

    private bool showAgain;

    #endregion

    #region Properties

    /// <summary>The address the screen is at, taken apart.</summary>
    public TalkAddress Address => address;

    /// <summary>
    /// What is beside the main conversation. Read from the address's own parts, so the rail and the address never
    /// disagree. A side chat that is neither new nor a thread falls back to the list.
    /// </summary>
    public SideView Side => (address.Rail, address.SideChat, ThreadId) switch
    {
        _ when address.WorkThreadId is not null => SideView.Work,
        (_, _, not null) => SideView.Chat,
        (_, NewSideChat, _) => SideView.New,
        (_, not null, _) or (SideRail, _, _) => SideView.List,
        (SummaryRail, _, _) => SideView.Summary,
        (ScreenRail, _, _) => SideView.Screen,
        (ActivityRail, _, _) => SideView.Activity,
        _ when address.ActivityRunId is not null => SideView.Activity,
        (FilesRail, _, _) => SideView.Files,
        (MemorySide, _, _) => SideView.Memory,
        _ => SideView.Closed,
    };

    /// <summary>A phone shows one of "crew", "main", and "side". The start page is the crew.</summary>
    public string At => (address.BotId, address.FormerThreadId, CanShowRail) switch
    {
        (null, null, _) => AtCrew,
        (_, _, true) => AtSide,
        _ => AtMain,
    };

    public bool CanShowRail => Side is not SideView.Closed && Shown is not null && NotReady is WorkspaceState.Ready;

    /// <summary>A screen, or a run as a timeline, needs room to be read: it shares the width evenly with the conversation.</summary>
    public bool HasWideRail => CanShowRail && Side is SideView.Screen or SideView.Activity;

    /// <summary>What stands in for the workspace until there is a bot to talk to.</summary>
    public WorkspaceState NotReady => this switch
    {
        { spaceProblem: not null } => WorkspaceState.Failed,
        { bots: null } => WorkspaceState.Loading,
        { connections.Count: 0 } => WorkspaceState.NoModel,
        { bots.Count: 0 } => WorkspaceState.NoBot,
        _ => WorkspaceState.Ready,
    };

    /// <summary>Why the space could not be loaded.</summary>
    public string? SpaceProblem => spaceProblem;

    /// <summary>Said, politely, while the space is read.</summary>
    public static string LoadingHint => "Loading your space.";

    /// <summary>
    /// What the main area says in place of a conversation: why there is none, and the one thing to do next. A phone
    /// shows it in the list while the workspace is not ready; a wide window shows it in the main area.
    /// </summary>
    public EmptyNotice? Notice => NotReady switch
    {
        WorkspaceState.Failed => new("Drover could not load this space", spaceProblem!, "Try again", null),
        WorkspaceState.NoModel => new(
            "No model is connected", "A bot needs a model to think with, and this space has none yet.", "Connect a model", Addresses.SetupModel),
        WorkspaceState.NoBot => new("No bot yet", "A model is connected, and there is no bot to use it.", "Meet your first bot", Addresses.SetupBot),
        WorkspaceState.Loading => null,
        _ when formerMissing => new("This conversation is not here", "It was removed, or it belongs to someone else.", "Back to the crew", Addresses.Home),
        _ when Shown is null => new("This bot is not here", "It was removed, or it belongs to another space.", "Back to the crew", Addresses.Home),
        _ => null,
    };

    /// <summary>What the rail says when the bot whose side of handed work was asked for is gone.</summary>
    public EmptyNotice? WorkNotice => Side is SideView.Work && WorkBot is null && Shown is { } shown
        ? new("This bot is not here", "It was removed, so its side of this work cannot be shown.", $"Back to {shown.Name}", Addresses.Talk(shown.Id))
        : null;

    /// <summary>The address kept from before names a conversation that is not there.</summary>
    public bool FormerMissing => formerMissing;

    /// <summary>
    /// Only a request the person took over here is held: opening the screen another way does not answer anything.
    /// The address names the request after a reload; it holds while that request still waits in the main conversation.
    /// </summary>
    public Takeover? HeldTakeover =>
        (openedTakeover ?? (mainTakeover is { } waiting && waiting.ToolCallId == address.Held ? waiting : null)) is { } held && held.ToolCallId != handedBack
            ? held
            : null;

    /// <summary>The message a link to a moment names, from the address's fragment.</summary>
    public Guid? Moment =>
        AddressParts.Split(navigator.Current).Fragment is { } fragment
        && fragment.StartsWith(MomentPrefix, StringComparison.Ordinal)
        && Guid.TryParse(fragment[MomentPrefix.Length..], out var id)
            ? id
            : null;

    /// <summary>Every bot of the space, once it has loaded.</summary>
    public IReadOnlyList<BotSummary>? Bots => bots;

    public IReadOnlyList<CrewNode> Crew => bots is null ? [] : CrewTree.From(bots);

    /// <summary>The bot being talked to. Asked for before the space has loaded too: the title is drawn outside the session gate.</summary>
    public BotSummary? Shown => (bots, address.BotId) switch
    {
        (null, _) => null,
        (_, { } id) => bots.FirstOrDefault(bot => bot.Id == id),
        _ => CrewTree.Lead(bots),
    };

    /// <summary>True while the shown bot works in its main conversation: its head in the header is lit.</summary>
    public bool MainBusy => mainBusy;

    /// <summary>The bots that are working for this person now. A bot working on work handed to it is working for them too.</summary>
    public IReadOnlySet<Guid> WorkingBots =>
        threads.Values
            .Where(thread => thread.ActiveRunId is not null && thread.Attention?.WaitingForYou is not true)
            .Select(thread => thread.BotId)
            .Concat(mainBusy && Shown is not null ? [Shown.Id] : [])
            .Concat(Handoffs.Where(pair => pair.Value is Handoff.Working).Select(pair => pair.Key))
            .ToHashSet();

    /// <summary>
    /// Work out wins over work that came back, so a bot both conversations handed work to shows it out. A side chat's
    /// work counts while the side chat is open.
    /// </summary>
    public IReadOnlyDictionary<Guid, Handoff> Handoffs =>
        mainHandoffs.Concat(Side is SideView.Chat or SideView.New ? sideHandoffs : [])
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Value));

    public BotSummary? WorkBot => bots?.FirstOrDefault(bot => bot.Id == address.WorkBotId);

    public string WorkName => WorkBot?.Name ?? Handover.Unknown;

    /// <summary>
    /// What each bot has for this person, across all their threads with it. A conversation on screen is being seen,
    /// so it has nothing new and no failure to point at, even before the server has heard so.
    /// </summary>
    public IReadOnlyDictionary<Guid, Attention> ForYou =>
        AttentionWords.OfCrew(threads.Values.Select(thread => IsOnScreen(thread) && thread.Attention is { } attention
            ? thread with { Attention = attention with { Failed = false, HasNew = false } }
            : thread));

    public IReadOnlyList<SideChatItem> SideChats => Shown is not { } shown
        ? []
        :
        [
            .. threads.Values
                .Where(thread => thread.BotId == shown.Id && !thread.IsMain)
                .OrderByDescending(thread => thread.LastActivityAt)
                .Select(thread => new SideChatItem(
                    thread.Id,
                    thread.Title ?? NewSideChatName,
                    Addresses.SideChat(thread.BotId, thread.Id),
                    shown.Name,
                    BotIdentity.Hue(thread.BotId),
                    thread.ActiveRunId is not null,
                    AttentionWords.OfCrew([thread]).GetValueOrDefault(thread.BotId))),
        ];

    public string SideChatsLabel => SideChats.Count switch
    {
        0 => "Side chats",
        1 => "1 side chat",
        var count => $"{count} side chats",
    };

    public string SideTitle => Side switch
    {
        SideView.Chat => threads.GetValueOrDefault(ThreadId!.Value)?.Title ?? "Side chat",
        SideView.Work => $"{WorkName}'s side",
        SideView.New => NewSideChatName,
        SideView.Summary => "Summary",
        SideView.Screen => "Screen",
        SideView.Activity => "Activity",
        SideView.Files => "Files",
        SideView.Memory => "Memory",
        _ => "Side chats",
    };

    /// <summary>The rail's accessible name.</summary>
    public string RailName => Side switch
    {
        SideView.Summary => $"Summary of the conversation with {Shown!.Name}",
        SideView.Screen => $"{Shown!.Name}'s screen",
        SideView.Activity => $"What {Shown!.Name} and its crew did in a run",
        SideView.Work => $"{WorkName}'s side of work handed to it",
        SideView.Files => $"Files exchanged with {Shown!.Name}",
        SideView.Memory => $"What {Shown!.Name} remembers about you",
        _ => $"Side chats with {Shown!.Name}",
    };

    public string CloseRailName => Side switch
    {
        SideView.Summary => "Close the summary",
        SideView.Screen => "Close the screen",
        SideView.Activity => "Close the activity",
        SideView.Work => $"Close {WorkName}'s side",
        SideView.Files => "Close the files",
        SideView.Memory => "Close memory",
        _ => "Close side chats",
    };

    /// <summary>True where the rail's first control closes it; a side chat's leads back to the list of them instead.</summary>
    public bool RailCloses =>
        Side is SideView.List or SideView.Summary or SideView.Memory or SideView.Screen or SideView.Files or SideView.Work or SideView.Activity;

    /// <summary>The name of the way from a side chat back to the others.</summary>
    public string AllSideChatsName => $"All side chats with {Shown?.Name}";

    /// <summary>What a side chat is, said above the list of them.</summary>
    public string SideChatsHint =>
        $"A side chat is a separate conversation with {Shown?.Name}. What is said there stays out of the main conversation.";

    /// <summary>The title of the window or tab. It says how many bots wait for the person, so it can be seen from another tab.</summary>
    public string PageName => AttentionWords.TabTitle(
        (bots, Shown, Side) switch
        {
            (null, _, _) or (_, null, _) => "Drover",
            (_, { } bot, SideView.Closed) => $"{bot.Name} · Drover",
            (_, { } bot, _) => $"{SideTitle} · {bot.Name} · Drover",
        },
        AttentionWords.Waiting(ForYou));

    /// <summary>The way to the bot's screen is offered: its sandbox has one this person may watch, or the screen is open.</summary>
    public bool OffersScreen => hasScreenLink || Side is SideView.Screen;

    /// <summary>Raised each time the bot stops working in the main conversation, so the summary on screen is read again.</summary>
    public int SummaryVersion { get; private set; }

    /// <summary>Raised each time the bot starts or stops working in the main conversation, so the activity rail finds the newest run.</summary>
    public int ActivityVersion { get; private set; }

    /// <summary>Changes each time a conversation with the shown bot has moved on, so the files rail reads its list again.</summary>
    public int ConversationVersion { get; private set; }

    /// <summary>The shown bot's main conversation. It stays while the address moves within this bot.</summary>
    public ConversationViewModel? MainPane { get; private set; }

    /// <summary>The side chat beside it: an existing one, or a new one its first message will make.</summary>
    public ConversationViewModel? SidePane { get; private set; }

    /// <summary>A bot's side of work handed to it, opened to read.</summary>
    public ConversationViewModel? WorkPane { get; private set; }

    /// <summary>The newest call to <see cref="Open"/>, as one task to wait for.</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    /// <summary>Raised when the focus belongs on the rail's close control: the controls the person used there are gone.</summary>
    public event Action? RailCloseFocusRequested;

    /// <summary>
    /// Raised once a rail closed from the keyboard is gone, with the way in the conversation's header that opens it
    /// again: the focus belongs there. Null when no way in the header opens it, as for work handed to another bot.
    /// </summary>
    public event Action<SideView?>? RailOpenerFocusRequested;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private Guid? ThreadId => Guid.TryParse(address.SideChat, out var id) ? id : null;

    private Guid? RailBotId => Shown?.Id ?? address.BotId;

    private Guid? HeldCallId => Side is SideView.Screen ? HeldTakeover?.ToolCallId : null;

    private Func<Guid, Guid, string> WorkAddress => (worker, thread) => Addresses.Work(Shown!.Id, worker, thread);

    #endregion

    #region Public Methods

    public void Dispose()
    {
        if (gone)
        {
            return;
        }

        gone = true;
        navigator.RemoveCloser(CloseRail);
        listening?.Cancel();
        listening?.Dispose();
        MainPane?.Dispose();
        SidePane?.Dispose();
        WorkPane?.Dispose();
    }

    /// <summary>The screen is at an address: the first one it opens at, or another of its own it moved to.</summary>
    public Task Open(TalkAddress opened)
    {
        address = opened;
        HoldRail();
        return Ready = Rendered(AddressSet());
    }

    /// <summary>Where the rail's first control leads: closed, or from a side chat back to the list of them.</summary>
    public string RailUp(Guid botId) => RailCloses ? Addresses.Talk(botId) : Addresses.SideChats(botId);

    /// <summary>Reads the space again, after it could not be read.</summary>
    public Task Retry() => Rendered(LoadSpace());

    /// <summary>Opens something beside the conversation, from the conversation's menu.</summary>
    public void OpenSide(string target) => navigator.NavigateTo(target);

    /// <summary>
    /// Closes what is beside the conversation, as Escape does, and asks for the focus to go back to the way that
    /// opened it. False when nothing is open beside it, so the key is left to whatever else wants it. A device's
    /// Back that closes the rail can call this too: what is open is read from the address, so either way leaves the
    /// screen in the same state.
    /// </summary>
    public bool CloseRailFromKeyboard()
    {
        if (!CanShowRail || Shown is not { } bot)
        {
            return false;
        }

        (railOpener, focusRailOpener) = (Side.Way(), true);
        navigator.NavigateTo(Addresses.Talk(bot.Id));
        return true;
    }

    /// <summary>The controls the person used are gone, so the focus goes to the screen's close control, the one way on.</summary>
    public void HandedBack(Guid toolCallId)
    {
        handedBack = toolCallId;
        openedTakeover = null;
        focusRailClose = true;
        Changed();
    }

    #endregion

    #region Loading

    private async Task AddressSet()
    {
        if (await session.Load() is null)
        {
            return;
        }

        // A sign-in at a tool server ends here, at the start page. The tool connections screen says how it went.
        if (address.ToolSignIn is "connected" or "failed")
        {
            navigator.NavigateTo(Addresses.ToolSignIn(address.ToolSignIn, address.ToolSignInConnection?.ToString()), replace: true);
            return;
        }

        if (bots is null && spaceProblem is null)
        {
            await LoadSpace();
        }

        if (address.FormerThreadId is { } former && bots is { Count: > 0 })
        {
            await FollowFormerAddress(former);
        }

        KeepOrRenewSidePane();

        // The conversation starts loading now, beside the question of the bot's screen, not after it.
        Changed();
        await AskForScreen();
    }

    private async Task LoadSpace()
    {
        spaceProblem = null;
        try
        {
            var connected = api.ListModelConnections(SpaceId);
            var listed = api.ListBots(SpaceId);
            await Task.WhenAll(connected, listed);
            connections = await connected;
            var found = await listed;
            await LoadThreads(found, CancellationToken.None);
            bots = found;
            Listen();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(TalkViewModel));
            spaceProblem = Failures.Sentence(exception);
        }
    }

    // Each thread arrives with what it has for this person, so the crew's first paint already says it.
    private async Task LoadThreads(IReadOnlyList<BotSummary> crew, CancellationToken cancellationToken)
    {
        Dictionary<Guid, ThreadSummary> found = [];
        foreach (var threadsOfBot in await Task.WhenAll(crew.Select(bot => api.ListThreads(SpaceId, bot.Id, cancellationToken))))
        {
            foreach (var thread in threadsOfBot)
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
            navigator.NavigateTo(thread.IsMain ? Addresses.Talk(thread.BotId) : Addresses.SideChat(thread.BotId, thread.Id), replace: true);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", threadId);
            formerMissing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(TalkViewModel));
            spaceProblem = Failures.Sentence(exception);
        }
    }

    #endregion

    #region Following Signals

    private bool IsOnScreen(ThreadSummary thread) =>
        thread.Id == ThreadId || (thread.IsMain && thread.BotId == Shown?.Id && address.FormerThreadId is null && At is not AtCrew);

    private void Listen()
    {
        if (listening is null)
        {
            listening = new();
            _ = FollowSignals(listening.Token);
        }
    }

    // Each signal names a thread that changed for this person, on this screen or another: it is read again, and the
    // crew and the title follow. A dropped connection may have missed some, so then every thread is read again.
    private async Task FollowSignals(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var signal in api.WatchSignals(SpaceId, cancellationToken: cancellationToken))
            {
                await Follow(signal, cancellationToken);
                Changed();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped following signals on the {Screen} screen", nameof(TalkViewModel));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // Refused, not dropped: connecting again would not change it. The crew still changes as this screen works.
            logger.LogWarning(exception, "The signal stream was refused on the {Screen} screen", nameof(TalkViewModel));
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
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound && signal.ThreadId is { } removed)
        {
            logger.LogInformation(exception, "Thread {ThreadId} is no longer visible on the {Screen} screen", removed, nameof(TalkViewModel));
            threads.Remove(removed);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // The crew keeps what it showed; the next signal or reconnection reads it again.
            logger.LogWarning(exception, "A thread could not be read again on the {Screen} screen", nameof(TalkViewModel));
        }
    }

    #endregion

    #region The Conversations On Screen

    // A conversation lives as long as what it was made for is on screen: the main one for its bot, a side chat for
    // its key, handed work for its thread. Each is told what the screen now says every time the screen changes.
    private void KeepConversations()
    {
        var shown = Shown;
        var ready = NotReady is WorkspaceState.Ready;
        var rail = CanShowRail && shown is not null;

        MainPane = Keep(
            MainPane,
            ref mainPaneKey,
            ready && !formerMissing && shown is not null && address.FormerThreadId is null ? shown.Id : null,
            () => new(shown!)
            {
                Main = true,
                Opened = At is not AtCrew,
                Moment = Side is SideView.Closed ? Moment : null,
                Crew = bots!,
                WorkAddress = WorkAddress,
                HeldCallId = HeldCallId,
                ScreenAccess = screenAccess,
                ScreenSetupHref = screenSetupHref,
            },
            pane =>
            {
                pane.OnThreadChanged = changed => Heard(() => Remember(changed));
                pane.OnBusyChanged = busy => Rendered(MainBusyChanged(busy));
                pane.OnHandoffsChanged = handoffs => Heard(() => mainHandoffs = handoffs);
                pane.OnOpenScreen = takeover => Heard(() => ScreenTakenOver(takeover));
                pane.OnTakeoverChanged = takeover => Heard(() => MainTakeoverChanged(takeover));
            });

        WorkPane = Keep(
            WorkPane,
            ref workPaneKey,
            rail && Side is SideView.Work && WorkBot is not null ? address.WorkThreadId : null,
            () => new(WorkBot!) { ThreadId = address.WorkThreadId, ReadOnly = true, Crew = bots!, WorkAddress = WorkAddress },
            _ => { });

        SidePane = Keep(
            SidePane,
            ref sidePaneKey,
            rail && Side is SideView.New or SideView.Chat ? sideKey : null,
            () => new(shown!)
            {
                ThreadId = ThreadId,
                Moment = Moment,
                Crew = bots!,
                WorkAddress = WorkAddress,
                ScreenAccess = screenAccess,
                ScreenSetupHref = screenSetupHref,
            },
            pane =>
            {
                pane.OnThreadStarted = made => Heard(() => SideChatStarted(made));
                pane.OnThreadChanged = changed => Heard(() => Remember(changed));
                pane.OnBusyChanged = busy => Rendered(SideBusyChanged(busy));
                pane.OnHandoffsChanged = handoffs => Heard(() => sideHandoffs = handoffs);
                pane.OnOpenScreen = takeover => Heard(() => ScreenTakenOver(takeover));
            });
    }

    private ConversationViewModel? Keep(
        ConversationViewModel? current,
        ref object? key,
        object? wanted,
        Func<ConversationParameters> parameters,
        Action<ConversationViewModel> listen)
    {
        if (wanted is null)
        {
            current?.Dispose();
            key = null;
            return null;
        }

        if (current is null || !Equals(key, wanted))
        {
            current?.Dispose();
            current = new(api, session, toasts, inbox, ui, loggers.CreateLogger<ConversationViewModel>(), parameters());
            listen(current);
            key = wanted;
        }

        _ = current.SetParameters(parameters());
        return current;
    }

    #endregion

    #region Private Methods

    // While a rail is open, a step back closes it before it leaves the conversation.
    private void HoldRail()
    {
        if (Side is SideView.Closed || RailBotId is null)
        {
            navigator.RemoveCloser(CloseRail);
        }
        else
        {
            navigator.AddCloser(CloseRail);
        }
    }

    private void CloseRail()
    {
        if (RailBotId is { } botId)
        {
            navigator.ReturnTo(RailUp(botId));
        }
    }

    private void ScreenTakenOver(Takeover takeover)
    {
        (openedTakeover, handedBack) = (takeover, null);

        if (Shown is { } bot)
        {
            navigator.NavigateTo($"{Addresses.Screen(bot.Id)}?held={takeover.ToolCallId}");
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
            logger.LogInformation(exception, "Whether bot {BotId} has a screen is not known on the {Screen} screen", bot.Id, nameof(TalkViewModel));
            screenAccess = screenAccess is ScreenAccess.Unknown ? ScreenAccess.Usable : screenAccess;
            hasScreenLink = false;
            screenKnownFor = bot.Id;
            return;
        }

        hasScreenLink = screenAccess is ScreenAccess.Usable;
        screenKnownFor = bot.Id;
    }

    // A different side chat gets a conversation of its own. The one exception is a new side chat whose first message
    // has just made its thread: the address changes and the conversation on screen stays as it is.
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

    private void SideChatStarted(ThreadSummary made)
    {
        startedThreadId = made.Id;
        threads[made.Id] = made;
        navigator.NavigateTo(Addresses.SideChat(made.BotId, made.Id), replace: true);
    }

    private void Remember(ThreadSummary changed) => threads[changed.Id] = changed;

    private async Task MainBusyChanged(bool busy)
    {
        mainBusy = busy;
        ActivityVersion++;
        if (!busy)
        {
            SummaryVersion++;
            ConversationVersion++;
            await RefreshCrew();
        }

        // A bot may have been given a screen, or started its sandbox, while it worked.
        screenAskedFor = null;
        await AskForScreen();
    }

    private Task SideBusyChanged(bool busy)
    {
        if (busy)
        {
            return Task.CompletedTask;
        }

        ConversationVersion++;
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
            logger.LogWarning(exception, "The crew could not be read again on the {Screen} screen", nameof(TalkViewModel));
        }
    }

    // What a conversation on screen told this screen is taken in, and the screen follows.
    private Task Heard(Action take)
    {
        take();
        Changed();
        return Task.CompletedTask;
    }

    // What a handler changed is shown when it first waits and again when it is done, as a screen does.
    private async Task Rendered(Task work)
    {
        if (!work.IsCompleted)
        {
            Changed();
        }

        try
        {
            await work;
        }
        finally
        {
            Changed();
        }
    }

    private void Changed() => ui.Post(Show);

    private void Show()
    {
        if (gone)
        {
            return;
        }

        if (showing)
        {
            showAgain = true;
            return;
        }

        showing = true;
        try
        {
            do
            {
                showAgain = false;
                KeepConversations();
            }
            while (showAgain);
        }
        finally
        {
            showing = false;
        }

        OnPropertyChanged(string.Empty);

        if (focusRailClose)
        {
            focusRailClose = false;
            RailCloseFocusRequested?.Invoke();
        }

        if (focusRailOpener && Side is SideView.Closed)
        {
            focusRailOpener = false;
            RailOpenerFocusRequested?.Invoke(railOpener);
        }
    }

    #endregion
}
