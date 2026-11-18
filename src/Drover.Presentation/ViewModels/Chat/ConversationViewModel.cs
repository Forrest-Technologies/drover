namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// One conversation with a bot, live: it loads what was said, follows the run that is answering, and sends what the
/// person writes. A bot's main conversation is opened by the bot alone; a side chat by its thread, or by nothing
/// when it is new and its first message makes it. The run itself is the server's: this only watches it and asks.
/// </summary>
public sealed class ConversationViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private readonly DroverApi api;

    private readonly SessionState session;

    private readonly ToastService toasts;

    private readonly ShareInbox inbox;

    private readonly IUiThread ui;

    private readonly ILogger<ConversationViewModel> logger;

    private readonly AttachmentDraft draft = new();

    // The notes forgotten from this conversation's cards, so each card says so, and says so no longer once undone.
    private readonly HashSet<Guid> forgottenNotes = [];

    // Uploads stop when the person leaves the conversation. The token is read once, at the start, so code that
    // resumes after the conversation is gone still reads it, and never the disposed source.
    private readonly CancellationTokenSource leaving = new();

    private ConversationParameters parameters;

    private IReadOnlyList<MessageResponse> stored = [];

    private AttachmentRules? rules;

    // The one request making this conversation's thread, while it is on its way. Sending and attaching both wait
    // for it, so a new side chat is made once however the first message and the first file race.
    private Task<ThreadSummary>? opening;

    private CancellationToken left;

    // The files of the message on its way, which leaving must not remove from under it.
    private IReadOnlyList<Guid> inFlight = [];

    private ImmutableDictionary<Guid, ToolCallView> history = [];

    private ThreadSummary? thread;

    private RunView? run;

    private string? reconnecting;

    private CancellationTokenSource? watch;

    private bool returnToComposer;

    private string? problem;

    private string? notice;

    // Loading until the first load has run, so the screen never says "Nothing said yet" about a conversation it has
    // not read. A new side chat, which has nothing to read, stops loading at once.
    private bool loading = true;

    private bool sending;

    private bool retrying;

    private bool markedOnOpen;

    private bool missing;

    private bool reportedBusy;

    private Takeover? reportedTakeover;

    private ImmutableDictionary<Guid, Handoff> reportedHandoffs = [];

    private bool started;

    private bool gone;

    #endregion

    #region Constructors

    public ConversationViewModel(
        DroverApi api,
        SessionState session,
        ToastService toasts,
        ShareInbox inbox,
        IUiThread ui,
        ILogger<ConversationViewModel> logger,
        ConversationParameters parameters)
    {
        (this.api, this.session, this.toasts, this.inbox, this.ui, this.logger, this.parameters) = (api, session, toasts, inbox, ui, logger, parameters);
        Composer.OnSend = text => Rendered(Send(text));
        Composer.OnCancel = () => Rendered(Stop());
        Composer.OnFiles = files => Rendered(Attach(files));
        Composer.OnRemoveFile = id => Rendered(RemoveFile(id));
        List.OnRetry = () => Rendered(Retry());
        List.Decide = Decide;
        List.Forget = Forget;
        List.ReadWhole = ReadWhole;
        List.LoadUsage = runId => api.GetRunUsage(SpaceId, runId);
        Messages = Build();
        ShowList();
        ShowComposer();
    }

    #endregion

    #region Properties

    /// <summary>The bot this conversation is with.</summary>
    public BotSummary Bot => parameters.Bot;

    public bool Main => parameters.Main;

    public bool ReadOnly => parameters.ReadOnly;

    /// <summary>The messages, the bot thinking, and how the last reply ended.</summary>
    public MessageListViewModel List { get; } = new();

    /// <summary>The field to write in. Not shown on a bot's side of work handed to it.</summary>
    public ComposerViewModel Composer { get; } = new();

    /// <summary>The side chat is not there: it was removed, or it belongs to someone else.</summary>
    public bool Missing => missing;

    public bool Loading => loading;

    /// <summary>The new conversation's first load, and each later change of what the screen tells it, as one task to wait for.</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// What is stored, then what the active run has said that is not stored yet. Each message appears once, with the
    /// tool calls it made: as the run being watched has them, or as the thread's past runs left them.
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

    public bool Working => run?.IsActive ?? false;

    public bool Thinking => run?.IsThinking() ?? false;

    /// <summary>True while the active run waits for a person. Nothing is happening then.</summary>
    public bool Waiting => run?.Status is "waiting";

    public string? Problem => problem;

    public string? Notice => reconnecting ?? notice;

    /// <summary>A bot's side of handed work was asked for by the bot above it, which wrote the task.</summary>
    public string Asker => parameters.Crew.FirstOrDefault(bot => bot.Id == Bot.ParentBotId)?.Name ?? Handover.Unknown;

    /// <summary>The conversation ends with its failed run until a run after it is under way.</summary>
    public RunFailure? Failure => Working ? null : thread?.Attention?.LastFailure;

    public bool Retrying => retrying;

    public bool Sending => sending;

    public IReadOnlySet<Guid> ForgottenNotes => forgottenNotes;

    /// <summary>The files chosen for the message being written, each with its state.</summary>
    public IReadOnlyList<AttachmentItem> Attachments => draft.Items;

    /// <summary>What a request for a person at the bot's screen needs from this conversation and the page around it.</summary>
    public ScreenTakeovers Takeovers => new(parameters.HeldCallId, parameters.ScreenAccess, parameters.ScreenSetupHref, OpenScreen, HandBack);

    /// <summary>In place of the field to write in, on a bot's side of work handed to it.</summary>
    public string ReadHint => Working ? $"{Bot.Name} is doing this for {Asker}." : $"{Bot.Name} did this for {Asker}. It is here to read.";

    /// <summary>What is said in place of a side chat that is not there, with the way on.</summary>
    public EmptyNotice MissingNotice =>
        new("This side chat is not here", "It was removed, or it belongs to someone else.", $"Back to {Bot.Name}", Addresses.Talk(Bot.Id));

    /// <summary>Said, politely, while the conversation is read.</summary>
    public static string LoadingHint => "Loading the conversation.";

    /// <summary>A new side chat got its thread from its first message.</summary>
    public Func<ThreadSummary, Task>? OnThreadStarted { get; set; }

    /// <summary>The thread's title, activity, or working run changed.</summary>
    public Func<ThreadSummary, Task>? OnThreadChanged { get; set; }

    /// <summary>The bot started or stopped working here. Waiting for a person's answer is not working.</summary>
    public Func<bool, Task>? OnBusyChanged { get; set; }

    /// <summary>The work this conversation's run has handed to other bots changed, by the bot that has it.</summary>
    public Func<IReadOnlyDictionary<Guid, Handoff>, Task>? OnHandoffsChanged { get; set; }

    /// <summary>The person took over the bot's screen for this request: the screen opens beside the conversation.</summary>
    public Func<Takeover, Task>? OnOpenScreen { get; set; }

    /// <summary>The request for the person at the bot's screen that waits here changed, or none waits any more.</summary>
    public Func<Takeover?, Task>? OnTakeoverChanged { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private bool Busy => Working && run?.Status is not "waiting";

    // Who reads what a reply used, by the same rule the server answers with.
    private UsageReader? Reader => session.Current is { } person
        ? new UsageReader(person.UserId, session.Space?.Role is "owner" or "admin")
        : null;

    #endregion

    #region Public Methods

    public void Dispose()
    {
        if (gone)
        {
            return;
        }

        gone = true;
        watch?.Cancel();
        watch?.Dispose();
        leaving.Cancel();
        leaving.Dispose();
        RemoveUnsentFiles();
    }

    /// <summary>
    /// The screen around the conversation changed, or shows it for the first time. The first call loads the
    /// conversation; each one after it only takes in what changed.
    /// </summary>
    public Task SetParameters(ConversationParameters changed)
    {
        parameters = changed;
        return Ready = Rendered(started ? ParametersSet() : StartThenParametersSet());
    }

    #endregion

    #region Loading

    private async Task StartThenParametersSet()
    {
        started = true;
        await Rendered(Start());
        await ParametersSet();
    }

    private async Task Start()
    {
        left = leaving.Token;

        // A new conversation has handed nothing out yet; the one shown before may have.
        if (!ReadOnly)
        {
            await Tell<IReadOnlyDictionary<Guid, Handoff>>(OnHandoffsChanged, reportedHandoffs);
        }

        if (!Main && parameters.ThreadId is null)
        {
            loading = false;
            return;
        }

        loading = true;
        try
        {
            thread = Main ? await api.OpenMainThread(SpaceId, Bot.Id) : await api.GetThread(SpaceId, parameters.ThreadId!.Value);
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
            markedOnOpen = parameters.Opened;
        }
        catch (DroverApiException exception) when (!Main && exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", parameters.ThreadId);
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
        }
    }

    // The start page's conversation becomes seen when the person chooses its bot: the conversation stays, the address changes.
    private async Task ParametersSet()
    {
        if (parameters.Opened && !markedOnOpen && thread is not null && !loading)
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

    // A reply that was under way when the conversation loaded is picked up again: its events are read from the first,
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

    // Starting a side chat changes the address, and the shell then moves focus to the screen. The person is in the
    // middle of a conversation, so the cursor goes back to the message field once that has happened.
    private async Task AfterShown()
    {
        if (returnToComposer && !missing && !ReadOnly)
        {
            returnToComposer = false;
            Composer.Focus();
        }

        await TakeWhatWasShared();
    }

    // What another app shared, once the person has chosen this bot for it, becomes this conversation's draft: the
    // words in the field, the files uploaded as any chosen file is. Sending stays the person's own act.
    private async Task TakeWhatWasShared()
    {
        if (!Main || ReadOnly || loading || missing || inbox.TakeFor(Bot.Id) is not { } shared)
        {
            return;
        }

        if (shared.Text.Length > 0)
        {
            Composer.Write(shared.Text);
        }

        if (shared.Files.Count > 0)
        {
            await Attach(shared.Files);
        }

        Changed();
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
            await ThreadChanged(thread);
            if (run is not { IsActive: true } || run.RunId != sent.Run.Id)
            {
                Watch(sent.Run.Id);
            }

            await ReportBusy();

            ShowNewest();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
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
        var made = await api.CreateThread(SpaceId, Bot.Id);
        returnToComposer = true;
        await Tell(OnThreadStarted, made);
        return made;
    }

    // The list shows the new message first, then scrolls to it.
    private void ShowNewest()
    {
        Changed();
        if (!missing && !loading)
        {
            ui.Post(List.ShowNewest);
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"The reply was not stopped. {Failures.Sentence(exception)}";
        }
    }

    // The takeover is recorded before the screen opens beside the conversation, so the audit log has it.
    private async Task<string?> OpenScreen(Takeover takeover)
    {
        var refused = await api.TakeOver(SpaceId, takeover.ToolCallId, logger);

        if (refused is null)
        {
            await Tell(OnOpenScreen, takeover);
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            return new(false, Problem: $"The note was not forgotten. {Failures.Sentence(exception)}");
        }
    }

    private async Task Restore(Guid noteId, string undo)
    {
        try
        {
            await api.RestoreMemoryNote(SpaceId, Bot.Id, undo);
            ui.Post(() => Forgotten(noteId, false));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            toasts.Show(exception is DroverApiException { StatusCode: HttpStatusCode.NotFound } ? MemoryWords.Gone : Failures.Sentence(exception), ToastKind.Error);
        }
    }

    private void Forgotten(Guid noteId, bool forgotten)
    {
        if (forgotten ? forgottenNotes.Add(noteId) : forgottenNotes.Remove(noteId))
        {
            Changed();
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
            var next = await api.RetryRun(SpaceId, failure.RunId);
            thread = thread with { ActiveRunId = next.Id, Attention = thread.Attention with { Failed = false, LastFailure = null } };
            await ThreadChanged(thread);
            Watch(next.Id);
            await ReportBusy();

            // The line and its button are gone; Stop, in the composer, took their place.
            returnToComposer = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
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
    private async Task Attach(IReadOnlyList<IPickedFile> files)
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
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
    private async Task Upload(IPickedFile file, Guid id, Guid threadId, long maxBytes)
    {
        var progress = OperatingSystem.IsBrowser() ? null : new SentSoFar(sent => ui.Post(() =>
        {
            if (draft.Progress(id, sent))
            {
                Show();
            }
        }));

        try
        {
            await using var content = await file.OpenRead(maxBytes, left);
            var uploaded = await api.UploadFile(
                SpaceId, threadId, file.Name, content, file.Size, progress, left);

            // Taken off while it was on its way, or the conversation was left: the server keeps nothing nobody will
            // send, so this removal goes ahead even after leaving.
            if (!draft.Uploaded(id, uploaded))
            {
                await api.RemoveFile(SpaceId, uploaded.Id, CancellationToken.None);
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
            // Whatever stopped it (the network, the server, the time limit, the file going away), the chip says it
            // was not uploaded and how to go on, and the message is held until it is taken off.
            logger.LogWarning(exception, "A file could not be uploaded in the {Screen}", nameof(ConversationViewModel));
            draft.Failed(id);
        }

        Changed();
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
    // would. An app that is closed cannot do this; those files stay until they are removed.
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
                Changed();

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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            (reconnecting, problem) = (null, $"Drover lost track of this work. {Failures.Sentence(exception)} Reload the page to see how it ended.");
        }

        Changed();
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
                Changed();
            }

            stored = await api.ListMessages(SpaceId, threadId, cancellationToken);
            (run, problem, notice) = (view, null, view.Notice());

            // A message sent just as the run ended is answered by a run of its own, which is followed next.
            thread = await api.GetThread(SpaceId, threadId, cancellationToken);
            await ThreadChanged(thread);
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
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            (run, reconnecting, problem) = (null, null, $"Drover lost track of this reply. {Failures.Sentence(exception)} Reload the page to see how it ended.");
        }

        await ReportBusy();
        Changed();
    }

    // The reader calls this from its own loop each time the connection is lost, then tries again.
    private void ConnectionDropped(Exception cause)
    {
        logger.LogInformation(cause, "The event stream dropped and is being reconnected");
        ui.Post(() =>
        {
            reconnecting = "The connection dropped. Reconnecting.";
            Show();
        });
    }

    #endregion

    #region Private Methods

    // Having the conversation open is seeing it, on every screen of the person's. If the server does not hear it, the
    // crew may still say there is something here until the next time; the conversation itself is unaffected.
    private async Task MarkSeen()
    {
        if (thread is null || !parameters.Opened || ReadOnly)
        {
            return;
        }

        try
        {
            await api.MarkThreadSeen(SpaceId, thread.Id);
            if (thread.Attention is { } attention)
            {
                thread = thread with { Attention = attention with { Failed = false, HasNew = false } };
                await ThreadChanged(thread);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Thread {ThreadId} could not be marked seen", thread.Id);
        }
    }

    private Task ThreadChanged(ThreadSummary changed) => Tell(OnThreadChanged, changed with { LastActivityAt = DateTimeOffset.UtcNow });

    // The crew column draws work passing along its connectors, so it is told each time that work moves. A run that is
    // followed from its start tells it from nothing; a new run starts from nothing again.
    private async Task ReportHandoffs()
    {
        var now = run?.Handoffs() ?? reportedHandoffs.Clear();

        if (now.Count != reportedHandoffs.Count || now.Any(pair => !reportedHandoffs.TryGetValue(pair.Key, out var before) || before != pair.Value))
        {
            reportedHandoffs = now;
            await Tell<IReadOnlyDictionary<Guid, Handoff>>(OnHandoffsChanged, now);
        }
    }

    // The screen around the conversation lights the bot's head while it works here, so it is told when that starts and
    // stops, and it shows the controls beside the bot's screen for a request to take it over, so it is told of that too.
    private async Task ReportBusy()
    {
        if (Busy != reportedBusy)
        {
            reportedBusy = Busy;
            await Tell(OnBusyChanged, reportedBusy);
        }

        var waiting = Build().AllTools().Select(tool => tool.Takeover).LastOrDefault(takeover => takeover is { Waits: true });

        if (waiting != reportedTakeover)
        {
            reportedTakeover = waiting;
            await Tell(OnTakeoverChanged, waiting);
        }
    }

    private static Task Tell<T>(Func<T, Task>? listener, T value) => listener?.Invoke(value) ?? Task.CompletedTask;

    private IReadOnlyList<ChatMessage> Build() => ChatTimeline.Merge(stored, run)
        .ToChatMessages(
            history.With(run),
            run,
            fileId => api.FileAddress(SpaceId, fileId),
            new Handover(Bot.Name, parameters.Crew.ToDictionary(bot => bot.Id, bot => bot.Name), parameters.WorkAddress),
            Reader);

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

    // Everything the screen reads is worked out here, once for each change, on the interface's thread.
    private void Show()
    {
        if (gone)
        {
            return;
        }

        Messages = Build();
        ShowList();
        ShowComposer();
        OnPropertyChanged(string.Empty);
        _ = AfterShown();
    }

    private void ShowList()
    {
        List.Messages = Messages;
        List.BotName = Bot.Name;
        List.BotHue = BotIdentity.Hue(Bot.Id);
        List.Thinking = Thinking;
        List.Asker = ReadOnly ? Asker : null;
        List.Problem = problem;
        List.Notice = Notice;
        List.ForgottenNotes = forgottenNotes;
        List.MemoryHref = Addresses.Memory(Bot.Id);
        List.Failure = Failure;
        List.Retrying = retrying;
        List.Moment = parameters.Moment;
        List.Takeovers = Takeovers;
        List.RunAddress = Main ? runId => Addresses.Activity(Bot.Id, runId) : null;
        List.Update();
    }

    private void ShowComposer()
    {
        Composer.BotName = Bot.Name;
        Composer.Running = Working;
        Composer.Waiting = Waiting;
        Composer.Disabled = loading;
        Composer.Sending = sending;

        var items = draft.Items;
        if (!items.SequenceEqual(Composer.Attachments))
        {
            Composer.Attachments = items;
        }
    }

    #endregion

    #region Nested Types

    private sealed class SentSoFar(Action<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard(value);
    }

    #endregion
}
