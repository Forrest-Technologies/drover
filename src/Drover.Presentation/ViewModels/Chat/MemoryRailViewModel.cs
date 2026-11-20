namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a bot remembers about the person reading, beside its conversation: the panel, filled from the server. It is
/// read again when <see cref="Version"/> changes, which is each time the bot stops working, as the summary is. A
/// removal offers Undo in a toast, which puts the note back exactly as it was.
/// </summary>
public sealed class MemoryRailViewModel(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    ILogger<MemoryRailViewModel> logger) : ObservableObject
{
    #region Private Fields

    private BotMemoryResponse? memory;

    private string? problem;

    private int? shownVersion;

    #endregion

    #region Properties

    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when the bot has stopped working, which is when it may have changed its notes.</summary>
    public int Version { get; set; }

    public IReadOnlyList<MemoryNote> Notes { get; private set; } = [];

    public bool NearlyFull => memory?.NearlyFull ?? false;

    public string? Problem => problem;

    public MemoryPanelState State => (memory, problem) switch
    {
        (_, not null) => MemoryPanelState.Failed,
        (null, _) => MemoryPanelState.Loading,
        ({ Enabled: false }, _) => MemoryPanelState.Off,
        _ => MemoryPanelState.Ready,
    };

    /// <summary>Only a person who may change the bot is sent to its settings.</summary>
    public string? EditorHref => session.Space?.Role is "owner" or "admin" ? Addresses.BotMemory(Bot.Id) : null;

    /// <summary>Raised when a note was put back, so the panel moves the focus to it.</summary>
    public event Action<Guid>? Restored;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>Runs once the bot and the version are set, and again each time one changes.</summary>
    public async Task ParametersSet()
    {
        if (shownVersion != Version)
        {
            shownVersion = Version;
            await Load();
        }
    }

    // The notes on screen stay while newer ones are fetched, so the list does not blink away after each reply. When
    // reading them again fails, they stay too, with any change being written, and a toast says the list may be old.
    public async Task Load()
    {
        problem = null;
        try
        {
            Show(await api.GetBotMemory(SpaceId, Bot.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRailViewModel));
            if (memory is null)
            {
                problem = Failures.Sentence(exception);
            }
            else if (toasts.Toasts.All(toast => toast.Message != StaleWarning(exception)))
            {
                toasts.Show(StaleWarning(exception), ToastKind.Warning);
            }
        }

        Changed();
    }

    public Task<string?> Add(string text) =>
        Saved(async () => await api.AddMemoryNote(SpaceId, Bot.Id, text));

    public Task<string?> Change(Guid noteId, string text) =>
        Saved(async () => await api.ChangeMemoryNote(SpaceId, Bot.Id, noteId, text));

    public async Task Remove(Guid noteId)
    {
        var removed = Notes.FirstOrDefault(note => note.Id == noteId);
        try
        {
            var receipt = await api.RemoveMemoryNote(SpaceId, Bot.Id, noteId);
            Notes = [.. Notes.Where(note => note.Id != noteId)];
            var text = removed?.Text ?? string.Empty;
            toasts.Show(
                MemoryWords.Removed(text),
                ToastKind.Info,
                new ToastAction("Undo", () => Restore(receipt.Undo), MemoryWords.UndoRemoving(text), receipt.UndoUntil));
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            // Removed already, by the bot or from a card: it is gone, as the person wanted, so there is nothing to undo.
            logger.LogInformation(exception, "Note {NoteId} was gone before it was removed", noteId);
            Notes = [.. Notes.Where(note => note.Id != noteId)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRailViewModel));
            toasts.Show(SentenceFor(exception), ToastKind.Error);
        }

        Changed();
    }

    #endregion

    #region Private Methods

    // Once saved the list is read again, so it says what the server now holds, in the server's order.
    private async Task<string?> Saved(Func<Task> save)
    {
        try
        {
            await save();
            await Load();
            return null;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRailViewModel));
            return SentenceFor(exception);
        }
    }

    // Undo is chosen in the toast, which then goes, so the focus moves to the note it put back.
    private async Task Restore(string undo)
    {
        try
        {
            var restored = await api.RestoreMemoryNote(SpaceId, Bot.Id, undo);
            await Load();
            Restored?.Invoke(restored.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRailViewModel));
            toasts.Show(SentenceFor(exception), ToastKind.Error);
        }
    }

    private void Show(BotMemoryResponse found)
    {
        memory = found;
        Notes = [.. found.Notes.Select(note => new MemoryNote(note.Id, note.Text, note.WrittenBy is "bot", note.ChangedAt, OriginOf(note)))];
    }

    private string? OriginOf(MemoryNoteResponse note) =>
        note.Origin is { } origin ? Addresses.Moment(Bot.Id, origin.ThreadId, origin.MessageId, origin.IsMain) : null;

    // Said once while it stands: toasts stay until dismissed, so a server that stays away does not stack them.
    private static string StaleWarning(Exception exception) =>
        $"The notes could not be read again, so they may be out of date. {Failures.Sentence(exception, "Check that the server is running.")}";

    private static string SentenceFor(Exception exception) =>
        exception is DroverApiException { StatusCode: HttpStatusCode.NotFound } ? MemoryWords.Gone : Failures.Sentence(exception, "Check that the server is running.");

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
