namespace Drover.Ui.Chat;

/// <summary>
/// One tool call as the conversation shows it. <see cref="Label"/> names what the bot did in plain words for the
/// call's phase ("Running a command", "Ran a command", "Run a command"), and <see cref="Request"/> is what it asks
/// to do, to follow "asks to". <see cref="Summary"/> is the arguments on one line: the command, the path, the point.
/// Arguments and result are JSON text as stored, already redacted. <see cref="ShowsAllArguments"/> is true when the
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

    /// <summary>The note a finished save kept, which the card offers to forget. Null on any other card.</summary>
    public Guid? NoteId { get; init; }

    /// <summary>What that note says, to name the control that forgets it.</summary>
    public string? NoteText { get; init; }

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
