namespace Drover.Ui.Chat;

/// <summary>
/// One conversation with a bot, live: it loads what was said, follows the run that is answering, and sends what the
/// person writes. A bot's main conversation is opened by the bot alone; a side chat by its thread, or by nothing
/// when it is new and its first message makes it.
/// </summary>
public sealed partial class ConversationPane(
    DroverApi api,
    SessionState session,
    ILogger<ConversationPane> logger) : IDisposable
{
    #region Private Fields

    private IReadOnlyList<MessageResponse> stored = [];

    private ImmutableDictionary<Guid, ToolCallView> history = [];

    private ThreadSummary? thread;

    private RunView? run;

    private string? reconnecting;

    private CancellationTokenSource? watch;

    private MessageList? list;

    private Composer? composer;

    private bool returnToComposer;

    private string? problem;

    private string? notice;

    private bool loading;

    private bool sending;

    private bool missing;

    private bool reportedBusy;

    #endregion

    #region Properties

    /// <summary>The bot this conversation is with.</summary>
    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>True for the bot's main conversation, which the server keeps one of for each person.</summary>
    [Parameter]
    public bool Main { get; set; }

    /// <summary>The side chat to open. Read once, when the pane is first shown.</summary>
    [Parameter]
    public Guid? ThreadId { get; set; }

    /// <summary>A new side chat got its thread from its first message.</summary>
    [Parameter]
    public EventCallback<ThreadSummary> OnThreadStarted { get; set; }

    /// <summary>The thread's title, activity, or working run changed.</summary>
    [Parameter]
    public EventCallback<ThreadSummary> OnThreadChanged { get; set; }

    /// <summary>The bot started or stopped working here. Waiting for a person's answer is not working.</summary>
    [Parameter]
    public EventCallback<bool> OnBusyChanged { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private bool Working => run?.IsActive ?? false;

    private bool Thinking => run?.IsThinking() ?? false;

    private bool Busy => Working && run?.Status is not "waiting";

    // What is stored, then what the active run has said that is not stored yet. Each message appears once, with the
    // tool calls it made: as the run being watched has them, or as the thread's past runs left them.
    private IReadOnlyList<ChatMessage> Messages => ChatTimeline.Merge(stored, run).ToChatMessages(history.With(run), run);

    private string? Notice => reconnecting ?? notice;

    #endregion

    #region Public Methods

    public void Dispose()
    {
        watch?.Cancel();
        watch?.Dispose();
    }

    #endregion

    #region Loading

    protected override async Task OnInitializedAsync()
    {
        if (!Main && ThreadId is null)
        {
            return;
        }

        loading = true;
        try
        {
            thread = Main ? await api.OpenMainThread(SpaceId, Bot.Id) : await api.GetThread(SpaceId, ThreadId!.Value);
            if (thread.BotId != Bot.Id)
            {
                (thread, missing) = (null, true);
                return;
            }

            stored = await api.ListMessages(SpaceId, thread.Id);
            history = await LoadToolHistory(thread);
            ResumeActiveRun(thread);
            await ReportBusy();
        }
        catch (DroverApiException exception) when (!Main && exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", ThreadId);
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            problem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
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

    // Starting a side chat changes the address, and the router then moves focus to the page heading. The person
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
            thread ??= Main ? await api.OpenMainThread(SpaceId, Bot.Id) : await StartSideChat();
            var sent = await api.SendMessage(SpaceId, thread.Id, text);
            stored = [.. stored, sent.Message];
            thread = thread with { Title = thread.Title ?? (thread.IsMain ? null : text), ActiveRunId = sent.Run.Id };
            await Changed(thread);
            if (run is not { IsActive: true } || run.RunId != sent.Run.Id)
            {
                Watch(sent.Run.Id);
            }

            await ReportBusy();

            await ShowNewest();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            problem = $"Your message was not sent. {Failures.Sentence(exception)}";
        }
        finally
        {
            sending = false;
        }
    }

    // The first message of a new side chat makes its thread, and the address follows.
    private async Task<ThreadSummary> StartSideChat()
    {
        var started = await api.CreateThread(SpaceId, Bot.Id);
        returnToComposer = true;
        await OnThreadStarted.InvokeAsync(started);
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            return $"Your answer was not sent. {Failures.Sentence(exception)}";
        }
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
                await ReportBusy();
                StateHasChanged();
            }

            stored = await api.ListMessages(SpaceId, threadId, cancellationToken);
            (run, problem, notice) = (view, view.Problem(), view.Notice());

            // A message sent just as the run ended is answered by a run of its own, which is followed next.
            thread = await api.GetThread(SpaceId, threadId, cancellationToken);
            await Changed(thread);
            if (thread.ActiveRunId is { } next && next != runId)
            {
                Watch(next);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The person went to another conversation or left. Nothing to show.
            logger.LogDebug("Stopped watching run {RunId}", runId);
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            (run, reconnecting, problem) = (null, null, $"Drover lost track of this reply. {Failures.Sentence(exception)} Reload the page to see how it ended.");
        }

        await ReportBusy();
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

    private Task Changed(ThreadSummary changed) => OnThreadChanged.InvokeAsync(changed with { LastActivityAt = DateTimeOffset.UtcNow });

    // The screen around the pane lights the bot's head while it works here, so it is told when that starts and stops.
    private async Task ReportBusy()
    {
        if (Busy != reportedBusy)
        {
            reportedBusy = Busy;
            await OnBusyChanged.InvokeAsync(reportedBusy);
        }
    }

    #endregion
}
