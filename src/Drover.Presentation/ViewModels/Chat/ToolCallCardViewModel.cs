namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What a card under a saved note offers: nothing, the one step that takes the note back, or the line that says how that ended.</summary>
public enum ToolNoteState
{
    None,
    Offered,
    Forgotten,
    ChangedSince,
}


/// <summary>
/// One tool call as a card in the bot's turn: what it reads closed, what it holds open, the answer a person gives a
/// call that waits for one, and the note a finished save offers to forget.
/// </summary>
public sealed class ToolCallCardViewModel : ObservableObject
{
    #region Private Fields

    private const int PreviewLength = 240;

    private bool open;

    private bool forgetting;

    private bool focusForgotten;

    #endregion

    #region Properties

    public ToolCard Card { get; set; } = null!;

    public string BotName { get; set; } = string.Empty;

    /// <summary>Sends a person's answer to a call that waits for one. Returns why it failed, or nothing when it was sent.</summary>
    public Func<Guid, bool, Task<string?>>? Decide { get; set; }

    /// <summary>Removes the note a save kept, from its card, while it still says what the card shows.</summary>
    public Func<Guid, string, Task<ForgetOutcome>>? Forget { get; set; }

    /// <summary>True once the card's note was forgotten, until that is undone.</summary>
    public bool Forgotten { get; set; }

    /// <summary>The bot's memory, where a note that changed since this card can be seen.</summary>
    public string? MemoryHref { get; set; }

    public bool Open
    {
        get => open;
        set
        {
            if (open != value)
            {
                open = value;
                Changed();
            }
        }
    }

    /// <summary>True while an answer is on its way, and after one that went through: the run's log turns the card into what happened next.</summary>
    public bool Deciding { get; private set; }

    /// <summary>Why the answer was not sent, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>The note changed after the bot saved it, so Forget left it alone.</summary>
    public bool ChangedSince { get; private set; }

    public string? ForgetProblem { get; private set; }

    /// <summary>A call waiting for a person shows as a question with two answers, not as a card that opens.</summary>
    public bool IsAsk => Card.Phase is ToolPhase.WaitingForYou;

    public string AskTitle => $"{BotName} asks to {Card.Request}";

    /// <summary>The arguments on one line under the title, or nothing when the call has none worth a line.</summary>
    public string? Summary => Card.Summary.Length > 0 ? Card.Summary : null;

    /// <summary>The question shows the arguments as written when its one line leaves some out.</summary>
    public bool AskShowsArguments => Card.Facts.Count == 0 && !Card.ShowsAllArguments;

    public bool ShowsFacts => Card.Facts.Count > 0;

    public bool ShowsArguments => !ShowsFacts && Card.Arguments is not ("{}" or "");

    public bool ShowsOutput => Card.Output.Count > 0;

    public bool ShowsResult => Card.ExitCode is not null || Card.Result is not null;

    public string? ExitLine => Card.ExitCode is { } exitCode ? $"Exit code {exitCode}" : null;

    /// <summary>A call the bot is working on that has printed nothing says so.</summary>
    public bool ShowsQuiet => Card.IsLive && Card.Output.Count == 0;

    /// <summary>The titles of the parts an open card holds, in order.</summary>
    public IReadOnlyList<string> PartTitles =>
    [
        .. ShowsArguments ? [ArgumentsTitle] : Array.Empty<string>(),
        .. ShowsOutput ? [OutputTitle] : Array.Empty<string>(),
        .. ShowsResult ? [ResultTitle] : Array.Empty<string>(),
    ];

    /// <summary>Why a command could not run, under the card whether it is open or closed.</summary>
    public string? ErrorLine => Card.CommandError;

    /// <summary>The first line a finished command printed, under the card while it is closed.</summary>
    public string? PreviewLine => Card.CommandError is null && !Open ? OutputPreview : null;

    /// <summary>The frame's top edge once the tool has ended, as a color token: green, red, or the color of an outcome nobody knows.</summary>
    public string? EndedEdge => Card.Phase switch
    {
        _ when Card.IsLive => null,
        ToolPhase.Done => "DvOk",
        ToolPhase.Failed => "DvDanger",
        ToolPhase.Interrupted => "DvWarn",
        _ => null,
    };

    /// <summary>True while the tool runs: the one time the line travels the card's top edge.</summary>
    public bool Running => Card.IsLive;

    public BadgeKind StateKind => Card.Phase switch
    {
        _ when Card.IsLive => BadgeKind.Live,
        ToolPhase.Done => BadgeKind.Ok,
        ToolPhase.Failed => BadgeKind.Danger,
        ToolPhase.Interrupted => BadgeKind.Warn,
        _ => BadgeKind.Neutral,
    };

    public ToolNoteState Note => (Card.NoteId, Forget) switch
    {
        (null, _) or (_, null) => ToolNoteState.None,
        _ when Forgotten => ToolNoteState.Forgotten,
        _ when ChangedSince => ToolNoteState.ChangedSince,
        _ => ToolNoteState.Offered,
    };

    public string ForgetName => $"Forget the note: {Card.NoteText}";

    public string ChangedSinceLine => MemoryWords.ChangedSince(BotName);

    private string? OutputPreview
    {
        get
        {
            if (Card.ExitCode is null || Card.Output.Count == 0)
            {
                return null;
            }

            var first = Card.Phase is ToolPhase.Failed
                ? Card.Output.FirstOrDefault(piece => piece.Stream is "stderr" && !string.IsNullOrWhiteSpace(piece.Text)) ?? Card.Output[0]
                : Card.Output[0];
            var text = first.Text.AsSpan().Trim();
            var end = text.IndexOfAny('\r', '\n');
            var length = Math.Min(end < 0 ? text.Length : end, PreviewLength);
            var line = text[..length].TrimEnd().ToString();
            return length < text.Length || Card.Output.Count > 1 ? $"{line}…" : line;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>Runs once the card's values are set, and again each time one changes.</summary>
    public void ParametersSet()
    {
        if (Card.Phase is not ToolPhase.WaitingForYou)
        {
            (Deciding, Problem) = (false, null);
        }
    }

    public async Task Answer(bool approved)
    {
        if (Decide is null)
        {
            return;
        }

        // The buttons stay off once the answer is sent: the run's log turns the card into what happened next.
        (Deciding, Problem) = (true, null);
        Changed();
        Problem = await Decide(Card.Id, approved);
        Deciding = Problem is null;
        Changed();
    }

    // The button is replaced by a line saying the note is gone, or that it changed since and so was kept, and the focus
    // moves to that line. The button stays enabled while the note is removed, so it keeps the focus if that fails; a
    // second press meanwhile does nothing.
    public async Task ForgetNote()
    {
        if (Forget is null || forgetting || Card.NoteId is not { } noteId)
        {
            return;
        }

        (forgetting, ForgetProblem) = (true, null);
        Changed();
        var outcome = await Forget(noteId, Card.NoteText ?? string.Empty);
        (forgetting, ChangedSince, ForgetProblem) = (false, outcome.Changed, outcome.Problem);
        focusForgotten = outcome.Forgotten || outcome.Changed;
        Changed();
    }

    /// <summary>
    /// True once, after the card has drawn the line that took the Forget button's place: the focus moves to that line.
    /// </summary>
    public bool TakeFocusForLine()
    {
        if (focusForgotten && (Forgotten || ChangedSince))
        {
            focusForgotten = false;
            return true;
        }

        return false;
    }

    #endregion

    #region Fixed Words

    public const string ArgumentsTitle = "Arguments";

    public const string OutputTitle = "Output";

    public const string ResultTitle = "Result";

    public const string Quiet = "No output yet.";

    public const string Approve = "Approve";

    public const string Deny = "Deny";

    public const string ForgetLabel = "Forget";

    public const string OpenMemory = "Open Memory to see it";

    public const string PreviewName = "Command output preview";

    #endregion

    #region Private Methods

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
