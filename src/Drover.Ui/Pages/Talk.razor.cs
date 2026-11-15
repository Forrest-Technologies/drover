namespace Drover.Ui.Pages;

/// <summary>
/// The workspace: the person picks a bot from the crew and talks to it in its main conversation. A side chat with
/// the same bot opens beside it.
/// </summary>
public sealed partial class Talk(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ILogger<Talk> logger)
{
    #region Private Fields

    private const string NewSideChatName = "New side chat";

    private const string SideRail = "side";

    private const string NewSideChat = "new";

    private readonly Dictionary<Guid, ThreadSummary> threads = [];

    private IReadOnlyList<BotSummary>? bots;

    private IReadOnlyList<ModelConnectionSummary>? connections;

    private string? spaceProblem;

    private bool formerMissing;

    private bool mainBusy;

    // The side pane is kept while a new side chat takes the address of the thread its first message made.
    private object sideKey = new();

    private string? shownSide;

    private Guid? startedThreadId;

    #endregion

    #region Properties

    /// <summary>The bot being talked to. Without one, the crew's lead is.</summary>
    [Parameter]
    public Guid? BotId { get; set; }

    /// <summary>"side" when the bot's side chats are listed beside its main conversation.</summary>
    [Parameter]
    public string? Rail { get; set; }

    /// <summary>The side chat that is open: its thread, or "new" for one its first message will make.</summary>
    [Parameter]
    public string? SideChat { get; set; }

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
        (_, _, not null) => SideView.Chat,
        (_, NewSideChat, _) => SideView.New,
        (_, not null, _) or (SideRail, _, _) => SideView.List,
        _ => SideView.Closed,
    };

    // A phone shows one of the crew, the main conversation, and the side chats. The start page is the crew.
    private string At => (BotId, FormerThreadId, Side) switch
    {
        (null, null, _) => "crew",
        (_, _, SideView.Closed) => "main",
        _ => "side",
    };

    private IReadOnlyList<CrewNode> Crew => CrewTree.From(bots!);

    private BotSummary? Shown => BotId is { } id ? bots!.FirstOrDefault(bot => bot.Id == id) : CrewTree.Lead(bots!);

    private IReadOnlySet<Guid> WorkingBots =>
        threads.Values
            .Where(thread => thread.ActiveRunId is not null)
            .Select(thread => thread.BotId)
            .Concat(mainBusy && Shown is not null ? [Shown.Id] : [])
            .ToHashSet();

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
                thread.ActiveRunId is not null)),
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
        SideView.New => NewSideChatName,
        _ => "Side chats",
    };

    private string PageName => (bots, Shown, Side) switch
    {
        (null, _, _) or (_, null, _) => "Drover",
        (_, { } bot, SideView.Closed) => $"{bot.Name} · Drover",
        (_, { } bot, _) => $"{SideTitle} · {bot.Name} · Drover",
    };

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
    }

    private async Task LoadSpace()
    {
        spaceProblem = null;
        try
        {
            connections = await api.ListModelConnections(SpaceId);
            var found = await api.ListBots(SpaceId);
            threads.Clear();
            foreach (var bot in found)
            {
                foreach (var thread in await api.ListThreads(SpaceId, bot.Id))
                {
                    threads[thread.Id] = thread;
                }
            }

            bots = found;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Talk));
            spaceProblem = Failures.Sentence(exception);
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

    #endregion

    #region Private Methods

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

    private void MainBusyChanged(bool busy) => mainBusy = busy;

    #endregion
}
