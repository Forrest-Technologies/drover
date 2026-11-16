namespace Drover.Ui.Chat;

/// <summary>
/// One conversation with a bot, live: it loads what was said, follows the run that is answering, and sends what the
/// person writes. A bot's main conversation is opened by the bot alone; a side chat by its thread, or by nothing
/// when it is new and its first message makes it.
/// </summary>
public sealed partial class ConversationPane(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    ILogger<ConversationPane> logger) : IDisposable
{
    #region Private Fields

    private readonly AttachmentDraft draft = new();

    // The notes forgotten from this conversation's cards, so each card says so, and says so no longer once undone.
    private readonly HashSet<Guid> forgottenNotes = [];

    private IReadOnlyList<MessageResponse> stored = [];

    private AttachmentRules? rules;

    // The one request making this conversation's thread, while it is on its way. Sending and attaching both wait
    // for it, so a new side chat is made once however the first message and the first file race.
    private Task<ThreadSummary>? opening;

    // Uploads stop when the person leaves the conversation. The token is read once, here, so code that resumes after
    // the pane is gone still reads it, and never the disposed source.
    private readonly CancellationTokenSource leaving = new();

    private CancellationToken left;

    // The files of the message on its way, which leaving must not remove from under it.
    private IReadOnlyList<Guid> inFlight = [];

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

    private bool retrying;

    private bool markedOnOpen;

    private bool missing;

    private bool reportedBusy;

    private Takeover? reportedTakeover;

    private ImmutableDictionary<Guid, Handoff> reportedHandoffs = [];

    #endregion

    #region Properties

    /// <summary>The bot this conversation is with.</summary>
    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>True for the bot's main conversation, which the server keeps one of for each person.</summary>
    [Parameter]
    public bool Main { get; set; }

    /// <summary>
    /// The person chose this conversation, so having it open is seeing it. False where the pane is there without being
    /// chosen: at the start page a phone shows the crew, not the lead's conversation.
    /// </summary>
    [Parameter]
    public bool Opened { get; set; } = true;

    /// <summary>The message a link points at, brought into view once the conversation is on the page.</summary>
    [Parameter]
    public Guid? Moment { get; set; }

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

    /// <summary>Every bot in the crew, so work handed to one is shown under its name.</summary>
    [Parameter]
    public IReadOnlyList<BotSummary> Crew { get; set; } = [];

    /// <summary>The address of a bot's side of work handed to it, from that bot and its thread. Without it, no link.</summary>
    [Parameter]
    public Func<Guid, Guid, string>? WorkAddress { get; set; }

    /// <summary>
    /// True for a bot's side of work handed to it, opened to read: there is no field to write in, and opening it marks
    /// nothing seen. The conversation that handed the work over shows it live.
    /// </summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>The work this conversation's run has handed to other bots changed, by the bot that has it.</summary>
    [Parameter]
    public EventCallback<IReadOnlyDictionary<Guid, Handoff>> OnHandoffsChanged { get; set; }

    /// <summary>The request whose screen the person took over and has open beside the conversation, if any.</summary>
    [Parameter]
    public Guid? HeldCallId { get; set; }

    /// <summary>Whether this person can open the bot's screen, for a request to take it over.</summary>
    [Parameter]
    public ScreenAccess ScreenAccess { get; set; }

    /// <summary>Where the missing screen is set up: the server's status page without a provider, the bot's settings otherwise.</summary>
    [Parameter]
    public string? ScreenSetupHref { get; set; }

    /// <summary>The person took over the bot's screen for this request: the screen opens beside the conversation.</summary>
    [Parameter]
    public EventCallback<Takeover> OnOpenScreen { get; set; }

    /// <summary>The request for the person at the bot's screen that waits here changed, or none waits any more.</summary>
    [Parameter]
    public EventCallback<Takeover?> OnTakeoverChanged { get; set; }

    // What a request for a person at the bot's screen needs from this conversation and the page around it.
    private ScreenTakeovers Takeovers => new(HeldCallId, ScreenAccess, ScreenSetupHref, OpenScreen, HandBack);

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private bool Working => run?.IsActive ?? false;

    private bool Thinking => run?.IsThinking() ?? false;

    private bool Busy => Working && run?.Status is not "waiting";

    // What is stored, then what the active run has said that is not stored yet. Each message appears once, with the
    // tool calls it made: as the run being watched has them, or as the thread's past runs left them.
    private IReadOnlyList<ChatMessage> Messages => ChatTimeline.Merge(stored, run)
        .ToChatMessages(
            history.With(run),
            run,
            fileId => api.FileAddress(SpaceId, fileId),
            new Handover(Bot.Name, Crew.ToDictionary(bot => bot.Id, bot => bot.Name), WorkAddress));

    private string? Notice => reconnecting ?? notice;

    // A bot's side of handed work was asked for by the bot above it, which wrote the task.
    private string Asker => Crew.FirstOrDefault(bot => bot.Id == Bot.ParentBotId)?.Name ?? Handover.Unknown;

    // The conversation ends with its failed run until a run after it is under way.
    private RunFailure? Failure => Working ? null : thread?.Attention?.LastFailure;

    #endregion

    #region Public Methods

    public void Dispose()
    {
        watch?.Cancel();
        watch?.Dispose();
        leaving.Cancel();
        leaving.Dispose();
        RemoveUnsentFiles();
    }

    #endregion

    #region Loading

    protected override async Task OnInitializedAsync()
    {
        left = leaving.Token;

        // A new conversation has handed nothing out yet; the one shown before may have.
        if (!ReadOnly)
        {
            await OnHandoffsChanged.InvokeAsync(reportedHandoffs);
        }

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

            if (ReadOnly)
            {
                await FollowHandedWork(thread.Id);
                return;
            }

            history = await LoadToolHistory(thread.Id, thread.ActiveRunId);

            ResumeActiveRun(thread);
            await ReportBusy();
            await MarkSeen();
            markedOnOpen = Opened;
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

    // The start page's conversation becomes seen when the person chooses its bot: the pane stays, the address changes.
    protected override async Task OnParametersSetAsync()
    {
        if (Opened && !markedOnOpen && thread is not null && !loading)
        {
            markedOnOpen = true;
            await MarkSeen();
        }
    }

    // Past tool calls are shown as their runs' logs left them. Without the log the conversation still reads: each call
    // is then shown as the stored messages tell it.
    private async Task<ImmutableDictionary<Guid, ToolCallView>> LoadToolHistory(Guid threadId, Guid? activeRunId, CancellationToken cancellationToken = default)
    {
        try
        {
            return ToolHistory.From(await api.ListToolEvents(SpaceId, threadId, cancellationToken), activeRunId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The tool history of thread {ThreadId} could not be loaded", threadId);
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
            thread = await OpenThread();
            var fileIds = inFlight = draft.FileIds;
            var sent = await api.SendMessage(SpaceId, thread.Id, text, fileIds: fileIds);

            // Only the files that went: one attached while the message was on its way stays for the next.
            draft.Sent(fileIds);
            stored = [.. stored, sent.Message];
            var title = thread.Title ?? (thread.IsMain || text.Length == 0 ? null : text);
            thread = thread with { Title = title, ActiveRunId = sent.Run.Id };
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
            inFlight = [];
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

    // The takeover is recorded before the screen opens beside the conversation, so the audit log has it.
    private async Task<string?> OpenScreen(Takeover takeover)
    {
        var refused = await api.TakeOver(SpaceId, takeover.ToolCallId, logger);

        if (refused is null)
        {
            await OnOpenScreen.InvokeAsync(takeover);
        }

        return refused;
    }

    private Task<string?> HandBack(Guid toolCallId, bool done, string? note) => api.HandBack(SpaceId, toolCallId, done, note, logger);

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

    // "Forget" on a note the bot saved in this conversation removes the note while it still says what the card shows;
    // the card then says so, and a toast offers Undo as the memory panel does.
    private async Task<ForgetOutcome> Forget(Guid noteId, string text)
    {
        try
        {
            var receipt = await api.ForgetMemoryNote(SpaceId, Bot.Id, noteId, text);
            Forgotten(noteId, true);
            toasts.Show(
                MemoryWords.Removed(text),
                ToastKind.Info,
                new ToastAction("Undo", () => Restore(noteId, receipt.Undo), MemoryWords.UndoRemoving(text), receipt.UndoUntil));
            return new(true);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            // Removed already, in the panel or by the bot: what the person asked for is so, and the card says it.
            logger.LogInformation(exception, "Note {NoteId} was gone before it was forgotten", noteId);
            Forgotten(noteId, true);
            return new(true);
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.Conflict)
        {
            logger.LogInformation(exception, "Note {NoteId} changed after its card was drawn", noteId);
            return new(false, Changed: true);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            return new(false, Problem: $"The note was not forgotten. {Failures.Sentence(exception)}");
        }
    }

    private async Task Restore(Guid noteId, string undo)
    {
        try
        {
            await api.RestoreMemoryNote(SpaceId, Bot.Id, undo);
            await InvokeAsync(() => Forgotten(noteId, false));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            toasts.Show(exception is DroverApiException { StatusCode: HttpStatusCode.NotFound } ? MemoryWords.Gone : Failures.Sentence(exception), ToastKind.Error);
        }
    }

    private void Forgotten(Guid noteId, bool forgotten)
    {
        if (forgotten ? forgottenNotes.Add(noteId) : forgottenNotes.Remove(noteId))
        {
            StateHasChanged();
        }
    }

    // The failed run's message is answered again by a new run, which is followed like any other.
    private async Task Retry()
    {
        if (retrying || thread?.Attention?.LastFailure is not { } failure)
        {
            return;
        }

        retrying = true;
        (problem, notice) = (null, null);
        try
        {
            var started = await api.RetryRun(SpaceId, failure.RunId);
            thread = thread with { ActiveRunId = started.Id, Attention = thread.Attention with { Failed = false, LastFailure = null } };
            await Changed(thread);
            Watch(started.Id);
            await ReportBusy();

            // The line and its button are gone; Stop, in the composer, took their place.
            returnToComposer = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            problem = $"It was not tried again. {Failures.Sentence(exception)}";
        }
        finally
        {
            retrying = false;
        }
    }

    #endregion

    #region Attaching Files

    private Task<FileText> ReadWhole(SharedFile file) => api.ReadFileText(SpaceId, file.Id, FileWords.MaxShownCharacters);

    // The conversation's thread, made now when it has none: the main one through its bot, a new side chat from its
    // first message or its first file, whichever comes first.
    private Task<ThreadSummary> OpenThread() =>
        thread is not null ? Task.FromResult(thread) : opening ??= MakeThread();

    private async Task<ThreadSummary> MakeThread()
    {
        try
        {
            thread = Main ? await api.OpenMainThread(SpaceId, Bot.Id) : await StartSideChat();
            return thread;
        }
        finally
        {
            opening = null;
        }
    }

    // A file goes to the server as soon as it is chosen, so Send has nothing left to wait for once each is ready. A
    // new side chat gets its thread now, from its first file, since a file belongs to a conversation.
    private async Task Attach(IReadOnlyList<IBrowserFile> files)
    {
        problem = null;
        ThreadSummary shown;
        try
        {
            shown = await OpenThread();
            rules ??= await api.GetAttachmentRules(SpaceId, shown.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            problem = $"The files were not attached. {Failures.Sentence(exception)}";
            return;
        }

        foreach (var file in files)
        {
            var item = draft.Add(file.Name, file.Size, rules);

            if (item.Phase is UploadPhase.Uploading)
            {
                _ = Upload(file, item.Id, shown.Id, rules.MaxFileBytes);
            }
        }
    }

    // How far a file has got is said only where it can be measured. In a browser the request is read whole before it
    // is sent, so a number would only say that, and the chip says "Uploading" until the server answers.
    private async Task Upload(IBrowserFile file, Guid id, Guid threadId, long maxBytes)
    {
        var progress = OperatingSystem.IsBrowser() ? null : new Progress<long>(sent =>
        {
            if (draft.Progress(id, sent))
            {
                _ = InvokeAsync(StateHasChanged);
            }
        });

        try
        {
            await using var content = file.OpenReadStream(maxBytes, left);
            var stored = await api.UploadFile(
                SpaceId, threadId, file.Name, content, file.Size, progress, left);

            // Taken off while it was on its way, or the conversation was left: the server keeps nothing nobody will
            // send, so this removal goes ahead even after leaving.
            if (!draft.Uploaded(id, stored))
            {
                await api.RemoveFile(SpaceId, stored.Id, CancellationToken.None);
            }
        }
        catch (DroverApiException exception) when (exception.Refusal is not null)
        {
            logger.LogInformation(exception, "The server refused a file: {Refusal}", exception.Refusal);
            draft.Refused(id, FileWords.RefusalFrom(exception.Refusal));
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("An upload stopped because the conversation was left");
            return;
        }
        catch (Exception exception)
        {
            // Whatever stopped it (the network, the server, the time limit, the browser's file going away), the chip
            // says it was not uploaded and how to go on, and the message is held until it is taken off.
            logger.LogWarning(exception, "A file could not be uploaded in the {Screen}", nameof(ConversationPane));
            draft.Failed(id);
        }

        await InvokeAsync(StateHasChanged);
    }

    // A file taken off before sending is removed from the server too, when it got there.
    private async Task RemoveFile(Guid id)
    {
        if (draft.Remove(id) is not { } fileId)
        {
            return;
        }

        try
        {
            await api.RemoveFile(SpaceId, fileId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A file taken off a message could not be removed from the server");
        }
    }

    // Leaving the conversation with files attached and not sent removes them from the server, as taking each off
    // would. A page that is closed or reloaded cannot do this; those files stay until they are removed.
    private void RemoveUnsentFiles()
    {
        foreach (var fileId in draft.FileIds.Except(inFlight))
        {
            _ = RemoveQuietly(fileId);
        }

        draft.Clear();
    }

    private async Task RemoveQuietly(Guid fileId)
    {
        try
        {
            await api.RemoveFile(SpaceId, fileId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A file left unsent could not be removed from the server");
        }
    }

    #endregion

    #region Watching A Run

    // A bot's side of handed work is one child run. While it is out, its calls are not settled as history: the child is
    // followed live, on its tree's stream, since a child run has no stream of its own. Once it has ended, what is
    // stored says everything.
    private async Task FollowHandedWork(Guid threadId)
    {
        var runId = stored.Select(message => message.RunId).FirstOrDefault(id => id is not null);
        var child = runId is { } id ? await FindRun(id) : null;

        if (child is null || child.Status is not ("queued" or "running" or "waiting"))
        {
            history = await LoadToolHistory(threadId, null);
            return;
        }

        history = await LoadToolHistory(threadId, child.Id);
        run = RunView.Start(child.Id);
        watch = new();
        _ = FollowUntilDone(threadId, child.RootRunId, child.Id, watch.Token);
    }

    // Without the run, the side still reads as its stored messages and log tell it.
    private async Task<RunSummary?> FindRun(Guid runId)
    {
        try
        {
            return await api.GetRun(SpaceId, runId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Run {RunId} could not be read to follow it", runId);
            return null;
        }
    }

    private async Task FollowUntilDone(Guid threadId, Guid rootRunId, Guid runId, CancellationToken cancellationToken)
    {
        var tree = RunView.Start(rootRunId);
        try
        {
            await foreach (var change in api.WatchRun(SpaceId, rootRunId, dropped: ConnectionDropped, cancellationToken: cancellationToken))
            {
                tree = RunReducer.Apply(tree, change);
                (run, reconnecting) = (tree.Find(runId) ?? run, null);
                StateHasChanged();

                if (run is { IsActive: false })
                {
                    break;
                }
            }

            stored = await api.ListMessages(SpaceId, threadId, cancellationToken);
            history = await LoadToolHistory(threadId, null, cancellationToken);
            run = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped following run {RunId}", runId);
            return;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationPane));
            (reconnecting, problem) = (null, $"Drover lost track of this work. {Failures.Sentence(exception)} Reload the page to see how it ended.");
        }

        StateHasChanged();
    }

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
                await ReportHandoffs();
                StateHasChanged();
            }

            stored = await api.ListMessages(SpaceId, threadId, cancellationToken);
            (run, problem, notice) = (view, null, view.Notice());

            // A message sent just as the run ended is answered by a run of its own, which is followed next.
            thread = await api.GetThread(SpaceId, threadId, cancellationToken);
            await Changed(thread);
            await MarkSeen();
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

    // Having the conversation open is seeing it, on every screen of the person's. If the server does not hear it, the
    // crew may still say there is something here until the next time; the conversation itself is unaffected.
    private async Task MarkSeen()
    {
        if (thread is null || !Opened || ReadOnly)
        {
            return;
        }

        try
        {
            await api.MarkThreadSeen(SpaceId, thread.Id);
            if (thread.Attention is { } attention)
            {
                thread = thread with { Attention = attention with { Failed = false, HasNew = false } };
                await Changed(thread);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Thread {ThreadId} could not be marked seen", thread.Id);
        }
    }

    private Task Changed(ThreadSummary changed) => OnThreadChanged.InvokeAsync(changed with { LastActivityAt = DateTimeOffset.UtcNow });

    // The crew column draws work passing along its connectors, so it is told each time that work moves. A run that is
    // followed from its start tells it from nothing; a new run starts from nothing again.
    private async Task ReportHandoffs()
    {
        var now = run?.Handoffs() ?? reportedHandoffs.Clear();

        if (now.Count != reportedHandoffs.Count || now.Any(pair => !reportedHandoffs.TryGetValue(pair.Key, out var before) || before != pair.Value))
        {
            reportedHandoffs = now;
            await OnHandoffsChanged.InvokeAsync(now);
        }
    }

    // The screen around the pane lights the bot's head while it works here, so it is told when that starts and stops,
    // and it shows the controls beside the bot's screen for a request to take it over, so it is told of that too.
    private async Task ReportBusy()
    {
        if (Busy != reportedBusy)
        {
            reportedBusy = Busy;
            await OnBusyChanged.InvokeAsync(reportedBusy);
        }

        var waiting = Messages.AllTools().Select(tool => tool.Takeover).LastOrDefault(takeover => takeover is { Waits: true });

        if (waiting != reportedTakeover)
        {
            reportedTakeover = waiting;
            await OnTakeoverChanged.InvokeAsync(waiting);
        }
    }

    #endregion
}
