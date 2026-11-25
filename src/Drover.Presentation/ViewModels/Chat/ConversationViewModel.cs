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

    // Remote reconciliation and local message changes replace or extend the same stored timeline. One finishes
    // before the next starts, so a late remote read cannot overwrite a message or version chosen meanwhile.
    private readonly SemaphoreSlim timelineGate = new(1, 1);

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

    private bool changing;

    private bool markedOnOpen;

    private bool missing;

    // The failure of the first read, or of a later try: different from a failure after a good read, which leaves the
    // messages on screen and says what failed under them.
    private string? loadProblem;

    private bool reportedBusy;

    private Takeover? reportedTakeover;

    private ImmutableDictionary<Guid, Handoff> reportedHandoffs = [];

    private bool started;

    private bool gone;

    private long seenThreadRevision;

    private long pendingThreadRevision;

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
        seenThreadRevision = pendingThreadRevision = parameters.ThreadRevision;
        Composer.OnSend = text => Rendered(Send(text));
        Composer.OnCancel = () => Rendered(Stop());
        Composer.OnFiles = files => Rendered(Attach(files));
        Composer.OnNotice = (words, kind) => toasts.Show(words, kind);
        Composer.OnRemoveFile = id => Rendered(RemoveFile(id));
        Composer.OnModelChosen = model => Rendered(ChooseModel(model));
        List.OnRetry = () => Rendered(Retry());
        List.OnShowVersion = messageId => Rendered(ShowVersion(messageId));
        List.OnRegenerate = replyId => Rendered(Regenerate(replyId));
        List.OnEdit = (messageId, text) => Rendered(Edit(messageId, text));
        List.Decide = Decide;
        List.Forget = Forget;
        List.ReadWhole = ReadWhole;
        List.LoadUsage = runId => api.GetRunUsage(SpaceId, runId);
        List.Logger = logger;
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

    /// <summary>The conversation's thread once it has one: a side chat has none until its first message.</summary>
    public Guid? ThreadId => thread?.Id;

    /// <summary>The messages, the bot thinking, and how the last reply ended.</summary>
    public MessageListViewModel List { get; } = new();

    /// <summary>The field to write in. Not shown on a bot's side of work handed to it.</summary>
    public ComposerViewModel Composer { get; } = new();

    /// <summary>The side chat is not there: it was removed, or it belongs to someone else.</summary>
    public bool Missing => missing;

    /// <summary>The conversation could not be read, so what is on screen is not what was said.</summary>
    public bool LoadFailed => loadProblem is not null;

    public bool Loading => loading;

    /// <summary>
    /// True while files chosen, pasted, dropped or shared would be taken. A conversation that is only read, is not
    /// there, is still being read, could not be read, or was left takes none.
    /// </summary>
    public bool AcceptsFiles => !gone && !ReadOnly && !Missing && !loading && !LoadFailed;

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

    /// <summary>What stands in for a conversation that could not be read, with the way to try again.</summary>
    public EmptyNotice LoadFailedNotice =>
        new("Drover could not load this conversation", loadProblem!, "Try again", null);

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
        Composer.TakesFiles = false;
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

    /// <summary>Reads a conversation that could not be read again. A second press while one is running does nothing.</summary>
    public Task Reload()
    {
        if (!LoadFailed || loading)
        {
            return Ready;
        }

        loadProblem = null;
        return Ready = ReadAgain();
    }

    #endregion

    #region Loading

    private async Task ReadAgain()
    {
        var revision = pendingThreadRevision;
        await Rendered(Start());
        if (!LoadFailed)
        {
            seenThreadRevision = Math.Max(seenThreadRevision, revision);
        }

        await ParametersSet();
    }

    private async Task StartThenParametersSet()
    {
        started = true;
        await Rendered(Start());
        await ParametersSet();
    }

    private async Task Start()
    {
        left = leaving.Token;

        // A try starts from nothing: what an earlier read left must not appear beside what this one reads.
        loadProblem = null;
        stored = [];
        history = [];
        thread = null;

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

            if (ReadOnly)
            {
                stored = await api.ListMessages(SpaceId, thread.Id);
                await FollowHandedWork(thread.Id);
                return;
            }

            // The messages, the tool calls they made, and the models it may answer with are read together; none
            // needs another.
            var said = api.ListMessages(SpaceId, thread.Id);
            var tools = LoadToolHistory(thread.Id, thread.ActiveRunId);
            var models = LoadModels(thread.Id);
            await Task.WhenAll(said, tools, models);
            (stored, history) = (await said, await tools);
            Composer.ShowModels(await models);

            ResumeActiveRun(thread);
            await ReportBusy();
            await ShownThenMarkSeen();
        }
        catch (DroverApiException exception) when (!Main && exception.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(exception, "Thread {ThreadId} was not found", parameters.ThreadId);
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            loadProblem = Failures.Sentence(exception);
        }
        finally
        {
            loading = false;
        }
    }

    // The conversation is on screen as soon as it is read; telling the server it was seen does not hold it back.
    private async Task ShownThenMarkSeen()
    {
        markedOnOpen = parameters.Opened;
        loading = false;
        Changed();
        await MarkSeen();
    }

    // The start page's conversation becomes seen when the person chooses its bot: the conversation stays, the address changes.
    private async Task ParametersSet()
    {
        pendingThreadRevision = Math.Max(pendingThreadRevision, parameters.ThreadRevision);
        if (parameters.Opened && !markedOnOpen && thread is not null && !loading)
        {
            markedOnOpen = true;
            await MarkSeen();
        }

        await TakeReportedChanges();
    }

    // A signal says to fetch again, never what changed. Revisions coalesce repeated signals while one read is in
    // flight. The timeline gate makes this read atomic with sends and version changes, and a later revision loops.
    private async Task TakeReportedChanges()
    {
        while (CanTakeReportedChanges() && pendingThreadRevision > seenThreadRevision)
        {
            var revision = pendingThreadRevision;
            try
            {
                await timelineGate.WaitAsync(left);
            }
            catch (OperationCanceledException) when (left.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!CanTakeReportedChanges() || revision <= seenThreadRevision)
                {
                    continue;
                }

                var latest = await api.GetThread(SpaceId, thread!.Id, left);
                var said = api.ListMessages(SpaceId, latest.Id, left);
                var historyRead = LoadToolHistory(latest.Id, latest.ActiveRunId, left);
                await Task.WhenAll(said, historyRead);
                (stored, history, thread, seenThreadRevision) = (await said, await historyRead, latest, revision);
                ClearSupersededRun();

                if (latest.ActiveRunId is { } runId && run?.RunId != runId)
                {
                    Watch(runId);
                }

                await ReportBusy();
                Changed();
                await MarkSeen();
            }
            catch (OperationCanceledException) when (left.IsCancellationRequested)
            {
                logger.LogDebug("Stopped taking in a change to thread {ThreadId}", thread?.Id);
                return;
            }
            catch (Exception exception) when (Failures.IsReportable(exception))
            {
                logger.LogWarning(
                    exception,
                    "A change to thread {ThreadId} could not be read in the {Screen}",
                    thread?.Id,
                    nameof(ConversationViewModel));
                return;
            }
            finally
            {
                timelineGate.Release();
            }
        }
    }

    private void ClearSupersededRun()
    {
        if (run is { IsActive: false } previous
            && previous.Messages.Any(message => !stored.Any(item => item.Id == message.Id)))
        {
            StopWatching();
            (run, notice) = (null, null);
        }
    }

    private bool CanTakeReportedChanges() =>
        thread is not null && !ReadOnly && !loading && !LoadFailed && !missing && !gone && !sending && !retrying
        && !changing && run is not { IsActive: true };

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

    // Choosing a model is a quiet option: when what it may choose cannot be read, the conversation reads and answers
    // as before, with the bot's own model, and offers no choice.
    private async Task<ConversationModelChoice?> LoadModels(Guid threadId)
    {
        try
        {
            return await api.GetConversationModels(SpaceId, threadId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The models of thread {ThreadId} could not be read", threadId);
            return null;
        }
    }

    // The server decides whether the bot may answer with the model. A refusal says why, and the choice goes back.
    private async Task ChooseModel(string? model)
    {
        if (thread is not { } shown)
        {
            return;
        }

        try
        {
            Composer.ShowModels(await api.ChooseConversationModel(SpaceId, shown.Id, model));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The model of thread {ThreadId} could not be chosen", shown.Id);
            toasts.Show(Failures.Sentence(exception, "Check that the server is running."), ToastKind.Error);
            Composer.ShowModels(await LoadModels(shown.Id));
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
    // words in the field, the files uploaded as any chosen file is. Sending stays the person's own act. A conversation
    // that takes no files now (it could not be read, say) leaves the share waiting for the next time it is opened.
    private async Task TakeWhatWasShared()
    {
        if (!Main || !AcceptsFiles || inbox.TakeFor(Bot.Id) is not { } shared)
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

    private async Task<bool> Send(string text)
    {
        sending = true;
        (problem, notice) = (null, null);
        var delivered = false;
        var enteredTimeline = false;
        try
        {
            await timelineGate.WaitAsync(left);
            enteredTimeline = true;
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
            delivered = true;
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("Stopped sending to thread {ThreadId}", thread?.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"Your message was not sent. {Failures.Sentence(exception)} It is back in the message field.";
        }
        finally
        {
            if (enteredTimeline)
            {
                timelineGate.Release();
            }

            sending = false;
            inFlight = [];
        }

        await TakeReportedChanges();
        return delivered;
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
            problem = $"The reply was not stopped. {Failures.Sentence(exception, "Check that the server is running.")}";
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
            return $"Your answer was not sent. {Failures.Sentence(exception, "Check that the server is running.")}";
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
            return new(false, Problem: $"The note was not forgotten. {Failures.Sentence(exception, "Check that the server is running.")}");
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
            toasts.Show(exception is DroverApiException { StatusCode: HttpStatusCode.NotFound } ? MemoryWords.Gone : Failures.Sentence(exception, "Check that the server is running."), ToastKind.Error);
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
        var enteredTimeline = false;
        try
        {
            await timelineGate.WaitAsync(left);
            enteredTimeline = true;
            var next = await api.RetryRun(SpaceId, failure.RunId);
            thread = thread with { ActiveRunId = next.Id, Attention = thread.Attention with { Failed = false, LastFailure = null } };
            await ThreadChanged(thread);
            Watch(next.Id);
            await ReportBusy();

            // The line and its button are gone; Stop, in the composer, took their place.
            returnToComposer = true;
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("Stopped retrying thread {ThreadId}", thread?.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"It was not tried again. {Failures.Sentence(exception, "Check that the server is running.")}";
        }
        finally
        {
            if (enteredTimeline)
            {
                timelineGate.Release();
            }

            retrying = false;
        }

        await TakeReportedChanges();
    }

    #endregion

    #region Versions

    private Task Regenerate(Guid replyId) =>
        StartVersion(thread => api.RegenerateReply(SpaceId, thread.Id, replyId), "No other reply was asked for.");

    private Task Edit(Guid messageId, string text) =>
        StartVersion(thread => api.EditMessage(SpaceId, thread.Id, messageId, text), "Your edit was not sent.");

    // A new version is answered by a new run, which is followed like any other. The version left is kept on the
    // server, so the conversation is read again to show the one that is now shown.
    private async Task StartVersion(Func<ThreadSummary, Task<SendMessageResponse>> ask, string notDone)
    {
        if (changing || thread is null)
        {
            return;
        }

        changing = true;
        (problem, notice) = (null, null);
        var enteredTimeline = false;
        try
        {
            await timelineGate.WaitAsync(left);
            enteredTimeline = true;
            var started = await ask(thread);
            stored = await api.ListMessages(SpaceId, thread.Id);
            thread = thread with
            {
                ActiveRunId = started.Run.Id,
                Attention = thread.Attention is { } attention ? attention with { Failed = false, LastFailure = null } : null,
            };
            await ThreadChanged(thread);
            Watch(started.Run.Id);
            await ReportBusy();
            ShowNewest();
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("Stopped changing thread {ThreadId}", thread?.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"{notDone} {Failures.Sentence(exception, "Check that the server is running.")}";
        }
        finally
        {
            if (enteredTimeline)
            {
                timelineGate.Release();
            }

            changing = false;
        }

        await TakeReportedChanges();
    }

    // The server shows the other version with what followed it; the conversation is read again to show it.
    private async Task ShowVersion(Guid messageId)
    {
        if (changing || thread is null)
        {
            return;
        }

        changing = true;
        problem = null;
        var enteredTimeline = false;
        try
        {
            await timelineGate.WaitAsync(left);
            enteredTimeline = true;
            await api.ShowMessageVersion(SpaceId, thread.Id, messageId);
            var said = api.ListMessages(SpaceId, thread.Id);
            var tools = LoadToolHistory(thread.Id, null);
            await Task.WhenAll(said, tools);
            (stored, history, run) = (await said, await tools, run is { IsActive: true } ? run : null);
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("Stopped changing thread {ThreadId}", thread?.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"The other version was not shown. {Failures.Sentence(exception, "Check that the server is running.")}";
        }
        finally
        {
            if (enteredTimeline)
            {
                timelineGate.Release();
            }

            changing = false;
        }

        await TakeReportedChanges();
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
        // However the files arrived, a conversation that does not take them (read only, missing, still being read, not
        // read, or left) takes none: the cursor refusing a drop is only a hint to the person. It is asked again after
        // each wait, since the person may leave while the thread or the rules are on their way.
        if (!AcceptsFiles)
        {
            return;
        }

        problem = null;
        ThreadSummary shown;
        try
        {
            shown = await OpenThread();

            if (!AcceptsFiles)
            {
                return;
            }

            rules ??= await api.GetAttachmentRules(SpaceId, shown.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationViewModel));
            problem = $"The files were not attached. {Failures.Sentence(exception, "Check that the server is running.")}";
            return;
        }

        if (!AcceptsFiles)
        {
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

        var stage = UploadStage.Open;
        try
        {
            await using var content = await file.OpenRead(maxBytes, left);
            stage = UploadStage.Transfer;
            var uploaded = file.Size is 0
                ? await api.UploadFileWithUnverifiedSize(SpaceId, threadId, file.Name, content, maxBytes, progress, left)
                : await api.UploadFile(SpaceId, threadId, file.Name, content, file.Size, progress, left);

            // Taken off while it was on its way, or the conversation was left: the server keeps nothing nobody will
            // send, so this removal goes ahead even after leaving.
            if (!draft.Uploaded(id, uploaded))
            {
                stage = UploadStage.Remove;
                await api.RemoveFile(SpaceId, uploaded.Id, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            logger.LogDebug("An upload stopped because the conversation was left");
            return;
        }
        catch (Exception exception)
        {
            var status = exception switch
            {
                DroverApiException refused => (int?)refused.StatusCode,
                HttpRequestException transport => (int?)transport.StatusCode,
                _ => null,
            };
            logger.LogWarning(
                "Attachment failed during {Stage} with {ExceptionType} (cause {CauseType}, HTTP {StatusCode})",
                stage, exception.GetType(), exception.InnerException?.GetBaseException().GetType(), status);
            var reason = exception is DroverApiException apiFailure
                ? FileWords.RefusalFrom(apiFailure.Refusal)
                : FileRefusal.None;
            draft.Refused(id, status == (int)HttpStatusCode.RequestEntityTooLarge ? FileRefusal.TooLarge : reason);
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
            await foreach (var change in api.WatchRun(SpaceId, rootRunId, dropped: ConnectionDropped, connected: ConnectionRestored, cancellationToken: cancellationToken))
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
            await foreach (var change in api.WatchRun(SpaceId, runId, dropped: ConnectionDropped, connected: ConnectionRestored, cancellationToken: cancellationToken))
            {
                view = RunReducer.Apply(view, change);
                (run, reconnecting, notice) = (view, null, view.Notice());
                await ReportBusy();
                await ReportHandoffs();
                Changed();
            }

            await timelineGate.WaitAsync(cancellationToken);
            try
            {
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
            finally
            {
                timelineGate.Release();
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
        await TakeReportedChanges();
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

    // The reader calls this each time it is through again. A run that is waiting on the person, or quiet, sends nothing
    // after a reconnect, so the notice goes now instead of with the next event.
    private void ConnectionRestored() => ui.Post(() =>
    {
        if (reconnecting is not null)
        {
            reconnecting = null;
            Show();
        }
    });

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

    // The same change around a handler's work, keeping what it answers for the composer to hear.
    private async Task<T> Rendered<T>(Task<T> work)
    {
        if (!work.IsCompleted)
        {
            Changed();
        }

        try
        {
            return await work;
        }
        finally
        {
            Changed();
        }
    }

    private IReadOnlyList<ChatMessage> Build() => ChatTimeline.Merge(stored, run)
        .ToChatMessages(
            history.With(run),
            run,
            fileId => api.FileAddress(SpaceId, fileId),
            new Handover(Bot.Name, parameters.Crew.ToDictionary(bot => bot.Id, bot => bot.Name), parameters.WorkAddress),
            Reader)
        .OfferingVersions(idle: !ReadOnly && !loading && !LoadFailed && !sending && !changing && !Working && thread is not null)
        .InTurns();

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
        List.Loading = loading;
        List.BotName = Bot.Name;
        List.BotHue = BotIdentity.Hue(Bot.Id);
        List.Thinking = Thinking;
        List.Working = Working;
        List.Waiting = Waiting;
        List.ModelIsChosen = Composer.ChosenModel != ComposerViewModel.BotsOwnModel;
        List.Model = List.ModelIsChosen ? Composer.ChosenModel : Bot.Model;
        List.Asker = ReadOnly ? Asker : null;
        List.Problem = problem;
        List.Notice = Notice;
        List.ForgottenNotes = forgottenNotes;
        List.MemoryHref = Addresses.Memory(Bot.Id);
        List.Failure = Failure;
        List.Retrying = retrying;
        List.Changing = changing;
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
        Composer.Disabled = loading || LoadFailed;
        Composer.TakesFiles = AcceptsFiles;
        Composer.Sending = sending;

        var items = draft.Items;
        if (!items.SequenceEqual(Composer.Attachments))
        {
            Composer.Attachments = items;
        }
    }

    #endregion

    #region Nested Types

    private enum UploadStage
    {
        Open,
        Transfer,
        Remove,
    }

    private sealed class SentSoFar(Action<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard(value);
    }

    #endregion
}
