namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What the activity panel has to show.</summary>
public enum ActivityPanelState
{
    Failed,
    Loading,
    NothingRan,
    Run,
}


/// <summary>
/// The activity rail: one run of a bot's main conversation, laid out as a timeline with a lane for each bot that
/// worked on it. It follows the newest run, read again each time <see cref="Version"/> changes, unless
/// <see cref="RunId"/> points it at an earlier one. The whole tree comes from the root run's one stream: stored
/// events first, then live ones, each once and in order. The thread's stored messages add the whole result of each
/// call the bot made itself.
/// </summary>
public sealed class ActivityPanelViewModel(
    DroverApi api,
    SessionState session,
    ILogger<ActivityPanelViewModel> logger) : ObservableObject, IDisposable
{
    #region Private Fields

    // Live output arrives many times a second; the timeline is drawn again at most this often.
    private static readonly TimeSpan redrawEvery = TimeSpan.FromMilliseconds(100);

    private IReadOnlyDictionary<Guid, PartView> results = new Dictionary<Guid, PartView>();

    private DateTimeOffset? startedAt;

    private CancellationTokenSource? watching;

    private bool loading = true;

    // True once the stream has ended: the run is over and everything kept of it has arrived.
    private bool watched;

    private bool redrawPending;

    // True when watching ended on a failure, so trying again opens the stream again even for the same run.
    private bool watchFailed;

    private (RunTreeView Tree, IReadOnlyDictionary<Guid, PartView> Results, IReadOnlyList<BotSummary> Bots, TimelineModel Model)? laid;

    private bool asList;

    private (Guid? RunId, int Version)? shown;

    #endregion

    #region Properties

    /// <summary>The bot whose main conversation's runs are shown.</summary>
    public BotSummary Bot { get; set; } = null!;

    /// <summary>The space's bots, so each lane is named.</summary>
    public IReadOnlyList<BotSummary> Bots { get; set; } = [];

    /// <summary>An earlier run to show instead of the newest.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Changes when the bot starts or stops working, which is when the newest run may have changed.</summary>
    public int Version { get; set; }

    /// <summary>The run shown, with every run beneath it, as its stream has told it so far.</summary>
    public RunTreeView? Tree { get; private set; }

    /// <summary>Stored results of calls, where the conversation has them.</summary>
    public IReadOnlyDictionary<Guid, PartView> Results => results;

    /// <summary>Why the run could not be loaded, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>True while the stream is being opened again after it dropped.</summary>
    public bool Reconnecting { get; private set; }

    /// <summary>The run as a list of sentences instead of lanes.</summary>
    public bool AsList
    {
        get => asList;
        set
        {
            if (asList != value)
            {
                asList = value;
                Changed();
            }
        }
    }

    public ActivityPanelState State => this switch
    {
        { Problem: not null } => ActivityPanelState.Failed,
        _ when loading || (Tree is { LastSeq: 0 } && !watched) => ActivityPanelState.Loading,
        { Tree: null } => ActivityPanelState.NothingRan,
        _ => ActivityPanelState.Run,
    };

    public string NothingRanCause => $"When {Bot.Name} answers a message, what it does shows here as it happens.";

    public string NothingRanAction => $"Write to {Bot.Name}";

    public string NothingRanHref => Addresses.Talk(Bot.Id);

    /// <summary>Where the newest run is, offered while an earlier one is shown.</summary>
    public string? NewestHref => RunId is null ? null : Addresses.Activity(Bot.Id);

    public string? ReconnectingNote => Reconnecting ? ReconnectingWords : null;

    public string? LostNote => Tree is { LostEarlierEvents: true } ? LostWords : null;

    public string TimelineLabel => $"Activity in this run of {Bot.Name}";

    // Laid out once for each new tree, not on every read: a live run redraws often and a long one has many nodes.
    public TimelineModel Model
    {
        get
        {
            if (Tree is null)
            {
                return TimelineModel.Empty;
            }

            if (laid is not { } last || !ReferenceEquals(last.Tree, Tree) || !ReferenceEquals(last.Results, results) || !ReferenceEquals(last.Bots, Bots))
            {
                laid = (Tree, results, Bots, RunTimelines.Lay(Tree, Bots.Select(bot => new TimelineBot(bot.Id, bot.Name)), results));
            }

            return laid.Value.Model;
        }
    }

    public string Facts
    {
        get
        {
            var root = Tree!.RunOf(Tree.RootRunId);
            var state = root.Status switch
            {
                "queued" or "running" => "Working",
                "waiting" => "Waiting for you",
                "completed" => "Finished",
                "failed" => "Failed",
                "cancelled" => "Cancelled",
                _ => "Working",
            };
            var which = RunId is null ? "The newest run" : "An earlier run";
            return startedAt is { } at ? $"{which} · {state} · started {ScheduleWords.Moment(at)}" : $"{which} · {state}";
        }
    }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    public void Dispose() => Stop();

    /// <summary>Runs once the panel's values are set, and again each time one changes.</summary>
    public async Task ParametersSet()
    {
        // Pointed at one run, the panel stays on it. Following the newest, it looks again when the bot's work changes.
        var wanted = (RunId, RunId is null ? Version : 0);
        if (shown != wanted)
        {
            shown = wanted;
            await Load();
        }
    }

    public async Task Load()
    {
        Problem = null;
        try
        {
            var thread = await api.OpenMainThread(SpaceId, Bot.Id);
            var messages = await api.ListMessages(SpaceId, thread.Id);
            results = ResultsIn(messages);
            var (rootRunId, rootBotId) = await FindRun(thread, messages);

            if (rootRunId != Tree?.RootRunId || watchFailed)
            {
                watchFailed = false;
                Stop();
                Tree = rootRunId is { } id ? RunTreeView.Start(id, rootBotId) : null;
                startedAt = null;
                watched = false;
                if (Tree is not null)
                {
                    watching = new();
                    _ = Watch(Tree.RootRunId, watching.Token);
                }
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ActivityPanelViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
            Changed();
        }
    }

    // The answer goes to the server's one decision endpoint, which checks who may give it. The run's stream then
    // brings the call's new state, here and in the conversation alike.
    public async Task<string?> Decide(Guid toolCallId, bool approved)
    {
        try
        {
            await api.DecideToolCall(SpaceId, toolCallId, approved);
            return null;
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.Conflict)
        {
            logger.LogInformation(exception, "Tool call {ToolCallId} was answered already", toolCallId);
            return "This request was answered already.";
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ActivityPanelViewModel));
            return Failures.Sentence(exception, "Check that the server is running.");
        }
    }

    public RunMoment? MomentOf(TimelineNode node) => Tree?.Moments.FirstOrDefault(moment => moment.Id == node.Event.Id);

    public string NameOf(int lane) => Model.Lanes is { } lanes && lane < lanes.Count ? lanes[lane].Name : string.Empty;

    #endregion

    #region Loading

    // The run asked for, or the thread's newest: the one working now, or the one that wrote the latest reply. A run
    // asked for by its reply may be a child's; its tree is the root's.
    private async Task<(Guid? RunId, Guid BotId)> FindRun(ThreadSummary thread, IReadOnlyList<MessageResponse> messages)
    {
        if (RunId is { } asked)
        {
            var run = await api.GetRun(SpaceId, asked);
            return run.RootRunId == run.Id ? (run.Id, run.BotId) : (run.RootRunId, (await api.GetRun(SpaceId, run.RootRunId)).BotId);
        }

        var newest = thread.ActiveRunId ?? messages.LastOrDefault(message => message is { RunId: not null, Position: not null })?.RunId;
        return (newest, Bot.Id);
    }

    private async Task Watch(Guid rootRunId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var stored in api.WatchRun(SpaceId, rootRunId, 0, Dropped, Connected, cancellationToken))
            {
                // An event of a stream already left behind must not reach the tree of the run shown now.
                if (cancellationToken.IsCancellationRequested || Tree?.RootRunId != rootRunId)
                {
                    break;
                }

                Tree = RunTreeReducer.Apply(Tree, stored);
                startedAt ??= stored.CreatedAt;
                Reconnecting = false;
                Redraw();
            }

            if (cancellationToken.IsCancellationRequested || Tree?.RootRunId != rootRunId)
            {
                return;
            }

            watched = true;
            Changed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogTrace("Stopped watching run {RunId}", rootRunId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception) && Tree?.RootRunId == rootRunId)
        {
            logger.LogWarning(exception, "Watching run {RunId} failed in the {Screen}", rootRunId, nameof(ActivityPanelViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
            watchFailed = true;
            Changed();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogDebug(exception, "A stream the {Screen} had left failed: run {RunId}", nameof(ActivityPanelViewModel), rootRunId);
        }
    }

    private void Dropped(Exception exception)
    {
        logger.LogDebug(exception, "The run's stream dropped in the {Screen}", nameof(ActivityPanelViewModel));
        Reconnecting = true;
        Redraw();
    }

    // Through again: a quiet run sends nothing after a reconnect, so the note goes now rather than with the next event.
    private void Connected()
    {
        if (Reconnecting)
        {
            Reconnecting = false;
            Redraw();
        }
    }

    private void Redraw()
    {
        if (redrawPending)
        {
            return;
        }

        redrawPending = true;
        _ = RedrawSoon();
    }

    private async Task RedrawSoon()
    {
        await Task.Delay(redrawEvery);
        redrawPending = false;
        Changed();
    }

    private void Stop()
    {
        watching?.Cancel();
        watching?.Dispose();
        watching = null;
    }

    #endregion

    #region Fixed Words

    public const string FailedTitle = "Drover could not load this run";

    public const string TryAgain = "Try again";

    public const string Loading = "Loading the run.";

    public const string NothingRanTitle = "Nothing has run in this conversation yet";

    public const string FormName = "Show the run as";

    public const string TimelineForm = "Timeline";

    public const string ListForm = "List";

    public const string NewestText = "Show the newest run";

    public const string ReconnectingWords = "The connection to the server dropped. Reconnecting.";

    public const string LostWords = "The start of this run is no longer kept, so its earliest events are not shown.";

    public const string EmptyTimeline = "This run's events are no longer kept. The messages are still in the conversation.";

    #endregion

    #region Private Methods

    private static Dictionary<Guid, PartView> ResultsIn(IEnumerable<MessageResponse> messages) =>
        messages
            .SelectMany(message => MessageView.From(message).Parts)
            .Where(part => part is { Kind: "tool_result", ToolCallId: not null })
            .GroupBy(part => part.ToolCallId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
