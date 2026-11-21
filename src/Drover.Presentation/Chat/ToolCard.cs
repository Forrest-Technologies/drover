namespace Drover.Ui.Chat;

/// <summary>
/// One tool call as the conversation shows it. <see cref="Label"/> names what the bot did in plain words for the
/// call's phase ("Running a command", "Ran a command", "Run a command"), and <see cref="Request"/> is what it asks
/// to do, to follow "asks to". <see cref="Summary"/> is the arguments on one line: the command, the path, the point.
/// Arguments are JSON text as stored, already redacted. <see cref="Result"/> is the result decoded for reading
/// (<see cref="ToolResults"/>): text as text, a command's output in <see cref="Output"/>, other values as JSON.
/// <see cref="ShowsAllArguments"/> is true when the
/// summary says everything the arguments hold.
/// </summary>
public sealed record ToolCard(
    Guid Id,
    ToolPhase Phase,
    string Label,
    string Request,
    string Summary,
    string Arguments,
    bool ShowsAllArguments,
    string? Result,
    int? ExitCode,
    ImmutableList<ToolOutputPiece> Output)
{
    /// <summary>
    /// What the call would do, as named facts a person can read without the JSON: for a new bot, its name, what it is
    /// for, its instructions. Empty for a call that has no such wording; then the arguments are shown as written.
    /// </summary>
    public IReadOnlyList<ToolFact> Facts { get; init; } = [];

    /// <summary>The language <see cref="Result"/> is written in, when it is known: <c>json</c>, or a file's extension.</summary>
    public string? ResultLanguage { get; init; }

    /// <summary>The length of the result as stored or as the call's events carried it, before it was decoded.</summary>
    public int ResultLength { get; init; }

    public string? CommandError { get; init; }

    /// <summary>The note a finished save kept, which the card offers to forget. Null on any other card.</summary>
    public Guid? NoteId { get; init; }

    /// <summary>What that note says, to name the control that forgets it.</summary>
    public string? NoteText { get; init; }

    /// <summary>The work this call handed to another bot, when it did or was refused; then the card is that work.</summary>
    public DelegationItem? Delegation { get; init; }

    /// <summary>Set for a call that asks a person to take over the bot's screen, which shows as its own card.</summary>
    public Takeover? Takeover { get; init; }

    /// <summary>True while the bot is working on the call.</summary>
    public bool IsLive => Phase is ToolPhase.Starting or ToolPhase.Approved or ToolPhase.Running;

    /// <summary>The phase in words, as the card shows it.</summary>
    public string State => Phase switch
    {
        ToolPhase.Starting => "Starting",
        ToolPhase.WaitingForYou => "Waiting for you",
        ToolPhase.Approved => "Approved",
        ToolPhase.Running => "Running",
        ToolPhase.Done => "Done",
        ToolPhase.Failed => "Failed",
        ToolPhase.Denied => "You declined",
        ToolPhase.NotAllowed => "Not allowed",
        ToolPhase.Interrupted => "Interrupted, outcome unknown",
        _ => "Stopped",
    };
}


/// <summary>
/// How "Forget" on a card ended: the note is gone, it changed after the bot saved it and so was left alone, or the
/// request failed and <see cref="Problem"/> says why.
/// </summary>
public sealed record ForgetOutcome(bool Forgotten, bool Changed = false, string? Problem = null);
