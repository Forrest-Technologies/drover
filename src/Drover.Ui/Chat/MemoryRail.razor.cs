namespace Drover.Ui.Chat;

/// <summary>
/// What a bot remembers about the person reading, beside its conversation: the panel, filled from the server. It is
/// read again when <see cref="Version"/> changes, which is each time the bot stops working, as the summary is. A
/// removal offers Undo in a toast, which puts the note back exactly as it was.
/// </summary>
public sealed partial class MemoryRail(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    ILogger<MemoryRail> logger)
{
    #region Private Fields

    private BotMemoryResponse? memory;

    private IReadOnlyList<MemoryNote> notes = [];

    private string? problem;

    private int? shownVersion;

    private MemoryPanel? panel;

    #endregion

    #region Properties

    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when the bot has stopped working, which is when it may have changed its notes.</summary>
    [Parameter]
    public int Version { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private MemoryPanelState State => (memory, problem) switch
    {
        (_, not null) => MemoryPanelState.Failed,
        (null, _) => MemoryPanelState.Loading,
        ({ Enabled: false }, _) => MemoryPanelState.Off,
        _ => MemoryPanelState.Ready,
    };

    // Only a person who may change the bot is sent to its settings.
    private string? EditorHref => session.Space?.Role is "owner" or "admin" ? Addresses.BotMemory(Bot.Id) : null;

    #endregion

    #region Private Methods

    protected override async Task OnParametersSetAsync()
    {
        if (shownVersion != Version)
        {
            shownVersion = Version;
            await Load();
        }
    }

    // The notes on screen stay while newer ones are fetched, so the list does not blink away after each reply. When
    // reading them again fails, they stay too, with any change being written, and a toast says the list may be old.
    private async Task Load()
    {
        problem = null;
        try
        {
            Show(await api.GetBotMemory(SpaceId, Bot.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRail));
            if (memory is null)
            {
                problem = Failures.Sentence(exception);
            }
            else if (toasts.Toasts.All(toast => toast.Message != StaleWarning(exception)))
            {
                toasts.Show(StaleWarning(exception), ToastKind.Warning);
            }
        }
    }

    private Task<string?> Add(string text) =>
        Saved(async () => await api.AddMemoryNote(SpaceId, Bot.Id, text));

    private Task<string?> Change(Guid noteId, string text) =>
        Saved(async () => await api.ChangeMemoryNote(SpaceId, Bot.Id, noteId, text));

    // Once saved the list is read again, so it says what the server now holds, in the server's order. The panel's
    // callback does not draw this component again by itself.
    private async Task<string?> Saved(Func<Task> save)
    {
        try
        {
            await save();
            await Load();
            StateHasChanged();
            return null;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRail));
            return SentenceFor(exception);
        }
    }

    private async Task Remove(Guid noteId)
    {
        var removed = notes.FirstOrDefault(note => note.Id == noteId);
        try
        {
            var receipt = await api.RemoveMemoryNote(SpaceId, Bot.Id, noteId);
            notes = [.. notes.Where(note => note.Id != noteId)];
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
            notes = [.. notes.Where(note => note.Id != noteId)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRail));
            toasts.Show(SentenceFor(exception), ToastKind.Error);
        }
    }

    // Undo is chosen in the toast, which then goes, so the focus moves to the note it put back.
    private async Task Restore(string undo)
    {
        try
        {
            var restored = await api.RestoreMemoryNote(SpaceId, Bot.Id, undo);
            await Load();
            await InvokeAsync(() =>
            {
                StateHasChanged();
                panel?.FocusNote(restored.Id);
            });
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(MemoryRail));
            toasts.Show(SentenceFor(exception), ToastKind.Error);
        }
    }

    private void Show(BotMemoryResponse found)
    {
        memory = found;
        notes = [.. found.Notes.Select(note => new MemoryNote(note.Id, note.Text, note.WrittenBy is "bot", note.ChangedAt, OriginOf(note)))];
    }

    private string? OriginOf(MemoryNoteResponse note) => note.Origin switch
    {
        null => null,
        { IsMain: true } origin => Addresses.TalkAt(Bot.Id, origin.MessageId),
        var origin => Addresses.SideChatAt(Bot.Id, origin.ThreadId, origin.MessageId),
    };

    // Said once while it stands: toasts stay until dismissed, so a server that stays away does not stack them.
    private static string StaleWarning(Exception exception) =>
        $"The notes could not be read again, so they may be out of date. {Failures.Sentence(exception)}";

    private static string SentenceFor(Exception exception) =>
        exception is DroverApiException { StatusCode: HttpStatusCode.NotFound } ? MemoryWords.Gone : Failures.Sentence(exception);

    #endregion
}
