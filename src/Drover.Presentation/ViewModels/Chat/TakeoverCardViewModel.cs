namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What a button of the takeover card does.</summary>
public enum TakeoverStep
{
    OpenScreen,
    Done,
    Decline,
}


/// <summary>One button of the takeover card. Only the one thing the person came to do is primary.</summary>
public sealed record TakeoverAction(TakeoverStep Step, string Label, bool Primary = false);


/// <summary>A run of words in the card's note. One with an address is a link.</summary>
public sealed record TakeoverWords(string Text, string? Href = null);


/// <summary>
/// A bot asks a person to take over its screen and waits. Three moments, one card: the ask, the person holding the
/// screen, and the answer given. Nothing moves while it waits: waiting is not a live state.
/// </summary>
public sealed class TakeoverCardViewModel : ObservableObject
{
    #region Private Fields

    private TakeoverPhase? seen;

    private bool focusDone;

    #endregion

    #region Properties

    public string BotName { get; set; } = string.Empty;

    /// <summary>Why the bot needs a person, in the bot's own short words.</summary>
    public string Reason { get; set; } = string.Empty;

    public TakeoverPhase Phase { get; set; }

    /// <summary>True while an answer is on its way to the server.</summary>
    public bool Sending { get; set; }

    /// <summary>Why the answer could not be sent, as a sentence.</summary>
    public string? Problem { get; set; }

    /// <summary>Whether this person can open the bot's screen. Until it is known the card offers no answer.</summary>
    public ScreenAccess Access { get; set; } = ScreenAccess.Usable;

    /// <summary>Where the bot's screen is set up, for a bot that has none now.</summary>
    public string? SetupHref { get; set; }

    /// <summary>The screen is open beside the conversation with its own copy of the controls, so this one only points there.</summary>
    public bool AnswerBesideScreen { get; set; }

    /// <summary>"I'm done" takes the keyboard focus when the card turns to holding, as the control that replaced "Open the screen".</summary>
    public bool TakeFocus { get; set; }

    /// <summary>True while the person writes the reason.</summary>
    public bool Declining { get; private set; }

    /// <summary>What the person chose to tell the bot about why they could not do it.</summary>
    public string Note { get; set; } = string.Empty;

    public bool Waits => Phase is TakeoverPhase.Asked or TakeoverPhase.Holding;

    public string Title => Phase switch
    {
        TakeoverPhase.Asked => $"{BotName} asks you to take over its screen",
        TakeoverPhase.Holding => $"You have {BotName}'s screen",
        TakeoverPhase.Done => $"You told {BotName} it is done",
        _ => $"You told {BotName} you could not do it",
    };

    public string State => Phase switch
    {
        TakeoverPhase.Asked => "Waiting for you",
        TakeoverPhase.Holding => $"{BotName} is waiting",
        TakeoverPhase.Done => "Done",
        _ => "Not done",
    };

    public BadgeKind StateKind => Waits ? BadgeKind.Warn : BadgeKind.Neutral;

    /// <summary>The frame's edge as a color token while the bot waits. The usual edge when null.</summary>
    public string? Edge => Waits ? "DvWarn" : null;

    /// <summary>What stands between the reason and the buttons: who uses the screen, where it is, or what is kept while it is held.</summary>
    public IReadOnlyList<TakeoverWords> NoteWords => (Phase, Access) switch
    {
        (TakeoverPhase.Asked, ScreenAccess.Usable) => [],
        (TakeoverPhase.Asked, ScreenAccess.NotPermitted) =>
            [new($"Owners and admins of this space use {BotName}'s screen. Ask one of them to do it, then tell {BotName} here.")],
        (TakeoverPhase.Asked, ScreenAccess.Missing) =>
        [
            new($"{BotName} has no screen now, so this cannot be done there."),
            .. SetupHref is { } setup ? [new(" "), new("Read its request.", setup)] : Array.Empty<TakeoverWords>(),
        ],
        (TakeoverPhase.Asked, ScreenAccess.Elsewhere) =>
        [
            new($"{BotName} works on a screen of its own. Open it from "),
            new("its sandbox page", SetupHref),
            new($", then tell {BotName} here."),
        ],
        (TakeoverPhase.Asked, _) => [new($"Checking whether you can use {BotName}'s screen.")],
        (TakeoverPhase.Holding, _) when AnswerBesideScreen => [new("Use I'm done at the screen when you have finished.")],
        (TakeoverPhase.Holding, _) =>
        [
            new($"Drover records nothing you type or click, and no bot can start anything new on this screen while you hold it. {BotName} sees the screen again when it next looks."),
        ],
        _ => [],
    };

    /// <summary>The note as one sentence, as it is read out.</summary>
    public string NoteText => string.Concat(NoteWords.Select(words => words.Text));

    /// <summary>The note is a status while the card is still finding out whether the person can use the screen.</summary>
    public bool NoteIsStatus => Phase is TakeoverPhase.Asked && Access is not (ScreenAccess.Usable or ScreenAccess.NotPermitted or ScreenAccess.Missing or ScreenAccess.Elsewhere);

    public IReadOnlyList<TakeoverAction> Actions => (Phase, Access) switch
    {
        (TakeoverPhase.Asked, ScreenAccess.Usable) => [new(TakeoverStep.OpenScreen, "Open the screen", Primary: true), new(TakeoverStep.Decline, "I cannot do it")],
        (TakeoverPhase.Asked, ScreenAccess.NotPermitted) => [new(TakeoverStep.Done, "It is done"), new(TakeoverStep.Decline, "I cannot do it")],
        (TakeoverPhase.Asked, ScreenAccess.Missing) => [new(TakeoverStep.Decline, "I cannot do it")],
        (TakeoverPhase.Asked, ScreenAccess.Elsewhere) => [new(TakeoverStep.Done, "It is done"), new(TakeoverStep.Decline, "I cannot do it")],
        (TakeoverPhase.Holding, _) when !AnswerBesideScreen => [new(TakeoverStep.Done, "I'm done", Primary: true), new(TakeoverStep.Decline, "I could not do it")],
        _ => [],
    };

    public bool ShowsDecline => Declining && Waits;

    public string DeclineLabel => $"Reason (optional, {BotName} reads it)";

    public string DeclineSubmit => $"Tell {BotName} I could not";

    public event Action? OpenScreen;

    public event Action? Done;

    /// <summary>The person could not do it. The text is what they chose to tell the bot, and may be empty.</summary>
    public event Action<string>? CouldNot;

    #endregion

    #region Public Methods

    /// <summary>Runs once the card's values are set, and again each time one changes.</summary>
    public void ParametersSet()
    {
        if (seen != Phase)
        {
            focusDone = TakeFocus && Phase is TakeoverPhase.Holding && !AnswerBesideScreen;
            (seen, Declining, Note) = (Phase, false, string.Empty);
        }
    }

    public void Press(TakeoverStep step)
    {
        switch (step)
        {
            case TakeoverStep.OpenScreen:
                OpenScreen?.Invoke();
                break;
            case TakeoverStep.Done:
                Done?.Invoke();
                break;
            default:
                Declining = true;
                Changed();
                break;
        }
    }

    public void GoBack()
    {
        Declining = false;
        Changed();
    }

    public void Decline()
    {
        Declining = false;
        Changed();
        CouldNot?.Invoke(Note.Trim());
    }

    /// <summary>True once, after the card has drawn "I'm done" in place of "Open the screen": the focus moves to it.</summary>
    public bool TakeFocusForDone()
    {
        if (focusDone && Actions.Any(action => action is { Step: TakeoverStep.Done, Primary: true }))
        {
            focusDone = false;
            return true;
        }

        return false;
    }

    #endregion

    #region Fixed Words

    public const string Back = "Back";

    #endregion

    #region Private Methods

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
