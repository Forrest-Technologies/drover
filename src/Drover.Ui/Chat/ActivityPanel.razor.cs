namespace Drover.Ui.Chat;

/// <summary>
/// The activity rail: one run of a bot's main conversation, laid out as a timeline with a lane for each bot that
/// worked on it. It follows the newest run, read again each time <see cref="Version"/> changes, unless
/// <see cref="RunId"/> points it at an earlier one. The whole tree comes from the root run's one stream: stored
/// events first, then live ones, each once and in order. The thread's stored messages add the whole result of each
/// call the bot made itself.
/// </summary>
public sealed partial class ActivityPanel(
    DroverApi api,
    SessionState session,
    ILogger<ActivityPanel> logger) : IDisposable
{
    #region Private Fields

    // Live output arrives many times a second; the timeline is drawn again at most this often.
    private static readonly TimeSpan redrawEvery = TimeSpan.FromMilliseconds(100);

    private static readonly IReadOnlyList<SegmentOption<bool>> forms = [new(false, "Timeline"), new(true, "List")];

    private RunTreeView? tree;

    private IReadOnlyDictionary<Guid, PartView> results = new Dictionary<Guid, PartView>();

    private DateTimeOffset? startedAt;

    private CancellationTokenSource? watching;

    private bool loading = true;

    private bool reconnecting;

    // True once the stream has ended: the run is over and everything kept of it has arrived.
    private bool watched;

    private bool redrawPending;

    // True when watching ended on a failure, so trying again opens the stream again even for the same run.
    private bool watchFailed;

    private (RunTreeView Tree, IReadOnlyDictionary<Guid, PartView> Results, IReadOnlyList<BotSummary> Bots, TimelineModel Model)? laid;

    private bool asList;

    private string? problem;

    private (Guid? RunId, int Version)? shown;

    #endregion

    #region Properties

    /// <summary>The bot whose main conversation's runs are shown.</summary>
    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>The space's bots, so each lane is named.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<BotSummary> Bots { get; set; } = [];

    /// <summary>An earlier run to show instead of the newest.</summary>
    [Parameter]
    public Guid? RunId { get; set; }

    /// <summary>Changes when the bot starts or stops working, which is when the newest run may have changed.</summary>
    [Parameter]
    public int Version { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // Laid out once for each new tree, not on every read: a live run redraws often and a long one has many nodes.
    private TimelineModel Model
    {
        get
        {
            if (tree is null)
            {
                return TimelineModel.Empty;
            }

            if (laid is not { } last || !ReferenceEquals(last.Tree, tree) || !ReferenceEquals(last.Results, results) || !ReferenceEquals(last.Bots, Bots))
            {
                laid = (tree, results, Bots, RunTimelines.Lay(tree, Bots.Select(bot => new TimelineBot(bot.Id, bot.Name)), results));
            }

            return laid.Value.Model;
        }
    }

    private string Facts
    {
        get
        {
            var root = tree!.RunOf(tree.RootRunId);
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

    #endregion

    #region Public Methods

    public void Dispose() => Stop();

    #endregion

    #region Loading

    protected override async Task OnParametersSetAsync()
    {
        // Pointed at one run, the panel stays on it. Following the newest, it looks again when the bot's work changes.
        var wanted = (RunId, RunId is null ? Version : 0);
        if (shown != wanted)
        {
            shown = wanted;
            await Load();
        }
    }

    private async Task Load()
    {
        problem = null;
        try
        {
            var thread = await api.OpenMainThread(SpaceId, Bot.Id);
            var messages = await api.ListMessages(SpaceId, thread.Id);
            results = ResultsIn(messages);
            var (rootRunId, rootBotId) = await FindRun(thread, messages);

            if (rootRunId != tree?.RootRunId || watchFailed)
            {
                watchFailed = false;
                Stop();
                tree = rootRunId is { } id ? RunTreeView.Start(id, rootBotId) : null;
                startedAt = null;
                watched = false;
                if (tree is not null)
                {
                    watching = new();
                    _ = Watch(tree.RootRunId, watching.Token);
                }
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ActivityPanel));
            problem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
        }
    }

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
                if (cancellationToken.IsCancellationRequested || tree?.RootRunId != rootRunId)
                {
                    break;
                }

                tree = RunTreeReducer.Apply(tree, stored);
                startedAt ??= stored.CreatedAt;
                reconnecting = false;
                Redraw();
            }

            if (cancellationToken.IsCancellationRequested || tree?.RootRunId != rootRunId)
            {
                return;
            }

            watched = true;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogTrace("Stopped watching run {RunId}", rootRunId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception) && tree?.RootRunId == rootRunId)
        {
            logger.LogWarning(exception, "Watching run {RunId} failed in the {Screen}", rootRunId, nameof(ActivityPanel));
            problem = Failures.Sentence(exception, "Check that the server is running.");
            watchFailed = true;
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogDebug(exception, "A stream the {Screen} had left failed: run {RunId}", nameof(ActivityPanel), rootRunId);
        }
    }

    private void Dropped(Exception exception)
    {
        logger.LogDebug(exception, "The run's stream dropped in the {Screen}", nameof(ActivityPanel));
        reconnecting = true;
        Redraw();
    }

    // Through again: a quiet run sends nothing after a reconnect, so the note goes now rather than with the next event.
    private void Connected()
    {
        if (reconnecting)
        {
            reconnecting = false;
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
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(redrawEvery);
            redrawPending = false;
            StateHasChanged();
        });
    }

    private void Stop()
    {
        watching?.Cancel();
        watching?.Dispose();
        watching = null;
    }

    #endregion

    #region Private Methods

    private static Dictionary<Guid, PartView> ResultsIn(IEnumerable<MessageResponse> messages) =>
        messages
            .SelectMany(message => MessageView.From(message).Parts)
            .Where(part => part is { Kind: "tool_result", ToolCallId: not null })
            .GroupBy(part => part.ToolCallId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());

    // The answer goes to the server's one decision endpoint, which checks who may give it. The run's stream then
    // brings the call's new state, here and in the conversation alike.
    private async Task<string?> Decide(Guid toolCallId, bool approved)
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ActivityPanel));
            return Failures.Sentence(exception, "Check that the server is running.");
        }
    }

    private RunMoment? MomentOf(TimelineNode node) => tree?.Moments.FirstOrDefault(moment => moment.Id == node.Event.Id);

    private string NameOf(int lane) => Model.Lanes is { } lanes && lane < lanes.Count ? lanes[lane].Name : string.Empty;

    #endregion
}
