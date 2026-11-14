namespace Drover.Ui.Pages;

/// <summary>
/// The workspace: the thread list and one conversation. The person reads what a bot said and writes to it.
/// </summary>
public sealed partial class Threads(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ILogger<Threads> logger) : IDisposable
{
    #region Private Fields

    private readonly List<ThreadSummary> threads = [];

    private IReadOnlyList<BotSummary>? bots;

    private IReadOnlyList<ModelConnectionSummary>? connections;

    private IReadOnlyList<MessageResponse> stored = [];

    private ImmutableDictionary<Guid, ToolCallView> history = [];

    private ThreadSummary? thread;

    private RunView? run;

    private string? reconnecting;

    private Guid? shownThreadId;

    private CancellationTokenSource? watch;

    private MessageList? list;

    private Composer? composer;

    private bool returnToComposer;

    private string? spaceProblem;

    private string? problem;

    private string? notice;

    private bool loadingThread;

    private bool sending;

    private bool missing;

    #endregion

    #region Properties

    [Parameter]
    public Guid? ThreadId { get; set; }

    /// <summary>Set when the browser comes back from a tool server's sign-in page: "connected" or "failed".</summary>
    [SupplyParameterFromQuery(Name = "tool-sign-in")]
    public string? ToolSignIn { get; set; }

    /// <summary>The tool connection that sign-in was for.</summary>
    [SupplyParameterFromQuery(Name = "connection")]
    public Guid? ToolSignInConnection { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // A phone shows the list or the conversation. The list is the start page; every other address is a conversation.
    private bool AtList => ThreadId is null && navigation.ToBaseRelativePath(navigation.Uri).Length == 0;

    private BotSummary? Bot => bots is [var first, ..] ? bots.FirstOrDefault(bot => bot.Status is "active") ?? first : null;

    private BotSummary ShownBot => bots!.FirstOrDefault(bot => bot.Id == thread?.BotId) ?? Bot!;

    private bool Working => run?.IsActive ?? false;

    private bool Thinking => run?.IsThinking() ?? false;

    // What is stored, then what the active run has said that is not stored yet. Each message appears once, with the
    // tool calls it made: as the run being watched has them, or as the thread's past runs left them.
    private IReadOnlyList<ChatMessage> Messages => ChatTimeline.Merge(stored, run).ToChatMessages(history.With(run), run);

    private string? Notice => reconnecting ?? notice;

    private string ThreadName => thread?.Title ?? "New thread";

    private string PageName => ThreadId is null ? "Drover" : $"{ThreadName} · Drover";

    private IReadOnlyList<ThreadListItem> ThreadItems =>
    [
        .. threads
            .OrderByDescending(item => item.LastActivityAt)
            .Select(item => new ThreadListItem(
                item.Id,
                item.Title ?? "New thread",
                BotNamed(item.BotId).Name,
                BotIdentity.Hue(item.BotId),
                item.Id == thread?.Id ? Working : item.ActiveRunId is not null)),
    ];

    #endregion

    #region Public Methods

    public void Dispose()
    {
        watch?.Cancel();
        watch?.Dispose();
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

        if (bots is { Count: > 0 } && ThreadId != shownThreadId)
        {
            await ShowThread(ThreadId);
        }
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
                threads.AddRange(await api.ListThreads(SpaceId, bot.Id));
            }

            bots = found;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            spaceProblem = Failures.Sentence(exception);
        }
    }

    private async Task Reload()
    {
        await LoadSpace();
        if (bots is { Count: > 0 })
        {
            await ShowThread(ThreadId);
        }
    }

    private async Task ShowThread(Guid? threadId)
    {
        StopWatching();
        shownThreadId = threadId;
        (thread, run, stored, problem, notice, reconnecting, missing) = (null, null, [], null, null, null, false);
        history = [];

        if (threadId is not { } id)
        {
            return;
        }

        loadingThread = true;
        try
        {
            thread = await api.GetThread(SpaceId, id);
            stored = await api.ListMessages(SpaceId, id);
            history = await LoadToolHistory(thread);
            ResumeActiveRun(thread);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", id);
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            problem = Failures.Sentence(exception);
        }
        finally
        {
            loadingThread = false;
        }
    }

    // Past tool calls are shown as their runs' logs left them. Without the log the conversation still reads: each call
    // is then shown as the stored messages tell it.
    private async Task<ImmutableDictionary<Guid, ToolCallView>> LoadToolHistory(ThreadSummary shown)
    {
        try
        {
            return ToolHistory.From(await api.ListToolEvents(SpaceId, shown.Id), shown.ActiveRunId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The tool history of thread {ThreadId} could not be loaded", shown.Id);
            return [];
        }
    }

    // A reply that was under way when the page loaded is picked up again: its events are read from the first,
    // so the text that had already arrived is rebuilt and the rest follows.
    private void ResumeActiveRun(ThreadSummary shown)
    {
        if (shown.ActiveRunId is { } runId)
        {
            Watch(runId);
        }
    }

    #endregion

    #region Sending

    // Starting a thread changes the address, and the router then moves focus to the page heading. The person
    // is in the middle of a conversation, so the cursor goes back to the message field once that has happened.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (returnToComposer && composer is not null)
        {
            returnToComposer = false;
            await Task.Yield();
            await composer.Focus();
        }
    }

    private async Task Send(string text)
    {
        sending = true;
        (problem, notice) = (null, null);
        try
        {
            thread ??= await StartThread();
            var sent = await api.SendMessage(SpaceId, thread.Id, text);
            stored = [.. stored, sent.Message];
            thread = thread with { Title = thread.Title ?? text, ActiveRunId = sent.Run.Id };
            Remember(thread);
            if (run is not { IsActive: true } || run.RunId != sent.Run.Id)
            {
                Watch(sent.Run.Id);
            }

            await ShowNewest();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            problem = $"Your message was not sent. {Failures.Sentence(exception)}";
        }
        finally
        {
            sending = false;
        }
    }

    // The first message of a new conversation makes its thread, and the address follows.
    private async Task<ThreadSummary> StartThread()
    {
        var started = await api.CreateThread(SpaceId, Bot!.Id);
        shownThreadId = started.Id;
        returnToComposer = true;
        Remember(started);
        navigation.NavigateTo(Addresses.Thread(started.Id), replace: true);
        return started;
    }

    // The list renders the new message first, then scrolls to it.
    private async Task ShowNewest()
    {
        StateHasChanged();
        await Task.Yield();
        if (list is not null)
        {
            await list.ShowNewest();
        }
    }

    private async Task Stop()
    {
        if (run is null)
        {
            return;
        }

        try
        {
            await api.CancelRun(SpaceId, run.RunId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            problem = $"The reply was not stopped. {Failures.Sentence(exception)}";
        }
    }

    // A person's answer to a tool call that waits for one. The run's log then shows what happened.
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
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            return $"Your answer was not sent. {Failures.Sentence(exception)}";
        }
    }

    private void Remember(ThreadSummary changed)
    {
        threads.RemoveAll(item => item.Id == changed.Id);
        threads.Add(changed with { LastActivityAt = DateTimeOffset.UtcNow });
    }

    #endregion

    #region Watching A Run

    // The run watched before keeps its tool calls on screen as history.
    private void Watch(Guid runId)
    {
        StopWatching();
        history = history.With(run);
        run = RunView.Start(runId);
        watch = new();
        _ = WatchUntilDone(thread!.Id, runId, watch.Token);
    }

    private void StopWatching()
    {
        watch?.Cancel();
        watch?.Dispose();
        watch = null;
    }

    // Reads the run's events as they happen and shows each change. The reader reconnects by itself and never
    // repeats or skips an event, so the text on screen is exactly what the bot has said so far.
    private async Task WatchUntilDone(Guid threadId, Guid runId, CancellationToken cancellationToken)
    {
        var view = RunView.Start(runId);
        try
        {
            await foreach (var change in api.WatchRun(SpaceId, runId, dropped: ConnectionDropped, cancellationToken: cancellationToken))
            {
                view = RunReducer.Apply(view, change);
                (run, reconnecting, notice) = (view, null, view.Notice());
                StateHasChanged();
            }

            stored = await api.ListMessages(SpaceId, threadId, cancellationToken);
            (run, problem, notice) = (view, view.Problem(), view.Notice());
            if (thread?.Id == threadId)
            {
                // A message sent just as the run ended is answered by a run of its own, which is followed next.
                thread = await api.GetThread(SpaceId, threadId, cancellationToken);
                Remember(thread);
                if (thread.ActiveRunId is { } next && next != runId)
                {
                    Watch(next);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The person opened another thread or left. Nothing to show.
            logger.LogDebug("Stopped watching run {RunId}", runId);
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(Threads));
            (run, reconnecting, problem) = (null, null, $"Drover lost track of this reply. {Failures.Sentence(exception)} Reload the page to see how it ended.");
        }

        StateHasChanged();
    }

    // The reader calls this from its own loop each time the connection is lost, then tries again.
    private void ConnectionDropped(Exception cause)
    {
        logger.LogInformation(cause, "The event stream dropped and is being reconnected");
        reconnecting = "The connection dropped. Reconnecting.";
        _ = InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Private Methods

    private BotSummary BotNamed(Guid botId) => bots!.FirstOrDefault(bot => bot.Id == botId) ?? Bot!;

    #endregion
}
