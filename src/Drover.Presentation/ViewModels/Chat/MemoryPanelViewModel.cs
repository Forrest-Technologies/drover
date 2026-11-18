namespace Drover.Presentation.ViewModels.Chat;

/// <summary>Where the focus goes once the memory panel has drawn: into the field, onto a note's Change button, or to adding.</summary>
public sealed record MemoryFocus(bool EditField = false, bool AddField = false, Guid? Note = null);


/// <summary>
/// What a bot remembers about the person reading, beside its conversation. Every note is readable, says where it
/// came from, and is changed in place. The panel has no filter and no folders: the notes are few by design, and a
/// list short enough to read needs neither.
/// </summary>
public sealed class MemoryPanelViewModel : ObservableObject
{
    #region Private Fields

    // Where the focus goes once the panel has drawn.
    private MemoryFocus? pendingFocus;

    // A note being removed, and where it stood, so the focus can stay in the list once it is gone.
    private (Guid Id, int Index)? removing;

    #endregion

    #region Properties

    public MemoryPanelState State { get; set; }

    public string BotName { get; set; } = string.Empty;

    /// <summary>The notes, newest first.</summary>
    public IReadOnlyList<MemoryNote> Notes { get; set; } = [];

    public bool NearlyFull { get; set; }

    /// <summary>Why loading failed, as a sentence.</summary>
    public string? Problem { get; set; }

    /// <summary>The editor's Memory section, for a person who may change the bot. Null for everyone else.</summary>
    public string? EditorHref { get; set; }

    /// <summary>
    /// Saves a new note. Returns why it was not saved, or nothing when it was. The caller updates <see cref="Notes"/>
    /// itself once the note is saved.
    /// </summary>
    public Func<string, Task<string?>>? OnAdd { get; set; }

    /// <summary>
    /// Saves a changed note. Returns why it was not saved, or nothing when it was. As with <see cref="OnAdd"/>, the
    /// caller updates <see cref="Notes"/> itself.
    /// </summary>
    public Func<Guid, string, Task<string?>>? OnChange { get; set; }

    /// <summary>The note being written in the field at the top.</summary>
    public string Adding { get; set; } = string.Empty;

    /// <summary>The note open for change, if one is.</summary>
    public Guid? Editing { get; private set; }

    /// <summary>The text of the note open for change, as it stands in its field.</summary>
    public string Draft { get; set; } = string.Empty;

    /// <summary>Why the last add or change was not saved. The text stays where it was typed, so nothing is lost.</summary>
    public string? SaveProblem { get; private set; }

    public string LoadingHint => $"Loading what {BotName} remembers.";

    public string FailedCause => Problem ?? "The server did not answer.";

    public string OffHint => Notes.Count is 0 ? MemoryWords.Off(BotName) : MemoryWords.OffKeeping(BotName);

    public string TurnOnText => $"Turn memory on in {BotName}'s settings";

    public string AddHint => $"Something {BotName} should know about you from now on.";

    public string? NearlyFullNote => NearlyFull ? MemoryWords.NearlyFull(BotName) : null;

    public string EmptyHint => MemoryWords.Empty(BotName);

    public string ListName => $"What {BotName} remembers about you";

    /// <summary>Removes at once. The caller offers Undo in a toast: a note is small and restoring it is exact.</summary>
    public event Action<Guid>? Remove;

    public event Action? Retry;

    #endregion

    #region Public Methods

    /// <summary>Who wrote a note and when: the line under it.</summary>
    public string Origin(MemoryNote note) => MemoryWords.Origin(note, BotName, ScheduleWords.Moment(note.When));

    public string OpenMomentName(MemoryNote note) => $"Open that moment: {BotName} noted {note.Text}";

    public static string ChangeName(MemoryNote note) => $"Change the note: {note.Text}";

    public static string RemoveName(MemoryNote note) => $"Remove the note: {note.Text}";

    /// <summary>Moves the focus to a note's Change button once the note is shown, as after it is put back.</summary>
    public void FocusNote(Guid noteId)
    {
        pendingFocus = new MemoryFocus(Note: noteId);
        Changed();
    }

    /// <summary>Runs once the panel's values are set, and again each time one changes.</summary>
    public void ParametersSet()
    {
        if (removing is { } removed && Notes.All(note => note.Id != removed.Id))
        {
            removing = null;
            pendingFocus = Notes.Count is 0 ? new MemoryFocus(AddField: true) : new MemoryFocus(Note: Notes[Math.Min(removed.Index, Notes.Count - 1)].Id);
        }
    }

    /// <summary>Where the focus goes now that the panel has drawn. It is given once.</summary>
    public MemoryFocus? TakeFocus()
    {
        var focus = pendingFocus;
        pendingFocus = null;
        return focus;
    }

    // The field empties only once the server has the note.
    public async Task Add()
    {
        if (string.IsNullOrWhiteSpace(Adding) || OnAdd is null)
        {
            return;
        }

        SaveProblem = await OnAdd(Adding.Trim());
        if (SaveProblem is null)
        {
            Adding = string.Empty;
        }

        Changed();
    }

    public void Edit(MemoryNote note)
    {
        (Editing, Draft, SaveProblem, pendingFocus) = (note.Id, note.Text, null, new MemoryFocus(EditField: true));
        Changed();
    }

    public void Cancel(MemoryNote note)
    {
        (Editing, SaveProblem, pendingFocus) = (null, null, new MemoryFocus(Note: note.Id));
        Changed();
    }

    public void RemoveNote(MemoryNote note)
    {
        removing = (note.Id, Notes.ToList().FindIndex(shown => shown.Id == note.Id));
        Remove?.Invoke(note.Id);
    }

    public void TryAgainNow() => Retry?.Invoke();

    // The note stays open for change until the server has it.
    public async Task Save(MemoryNote note)
    {
        if (string.IsNullOrWhiteSpace(Draft) || OnChange is null)
        {
            return;
        }

        SaveProblem = await OnChange(note.Id, Draft.Trim());
        if (SaveProblem is null)
        {
            (Editing, pendingFocus) = (null, new MemoryFocus(Note: note.Id));
        }

        Changed();
    }

    #endregion

    #region Fixed Words

    public const string FailedTitle = "Drover could not load the notes";

    public const string TryAgain = "Try again";

    public const string AddLabel = "Add a note";

    public const string AddAction = "Add the note";

    public const string EditLabel = "Note";

    public const string SaveAction = "Save";

    public const string CancelAction = "Cancel";

    public const string ChangeAction = "Change";

    public const string RemoveAction = "Remove";

    public const string OpenMoment = "Open that moment";

    #endregion

    #region Private Methods

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
