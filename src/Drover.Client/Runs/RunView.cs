namespace Drover.Client.Runs;

/// <summary>
/// What a screen renders for one run: its status, the messages it has produced so far, and its tool calls. It is
/// built only by <see cref="RunReducer"/> from the run's events, so the same events always give the same view,
/// whether they arrived live, after a reconnect, or all at once from a finished run.
/// </summary>
public sealed record RunView
{
    #region Properties

    public required Guid RunId { get; init; }

    /// <summary>queued, running, waiting, completed, failed, or cancelled.</summary>
    public string Status { get; init; } = "queued";

    public string? Reason { get; init; }

    public RunProblem? Error { get; init; }

    public int Attempt { get; init; }

    /// <summary>The last event applied. Pass it to <c>WatchRun</c> to go on from here.</summary>
    public long LastSeq { get; init; }

    public UsageTotals Usage { get; init; } = new(0, 0, 0, 0m);

    /// <summary>The run's assistant messages in order, the one being streamed last.</summary>
    public ImmutableList<MessageView> Messages { get; init; } = [];

    /// <summary>The run's tool calls in the order the model asked for them.</summary>
    public ImmutableList<ToolCallView> ToolCalls { get; init; } = [];

    /// <summary>True until the run has completed, failed, or been cancelled.</summary>
    public bool IsActive => Status is "queued" or "running" or "waiting";

    #endregion

    #region Public Methods

    public static RunView Start(Guid runId) => new() { RunId = runId };

    /// <summary>The view of this run or of one it handed work to, at any depth, or null.</summary>
    public RunView? Find(Guid runId) =>
        RunId == runId
            ? this
            : ToolCalls.Select(tool => tool.Delegation?.Child.Find(runId)).FirstOrDefault(found => found is not null);

    /// <summary>True when the run is this one or one it handed work to, at any depth.</summary>
    public bool Holds(Guid runId) =>
        RunId == runId || ToolCalls.Any(tool => tool.Delegation?.Child.Holds(runId) is true);

    #endregion
}


/// <summary>
/// One message as a screen shows it. <see cref="IsStreaming"/> is true while its text is still arriving; a streamed
/// message is replaced by the stored one when it completes, and dropped if its step is discarded.
/// <see cref="IsPending"/> is true for a message sent while the bot was working that no run has taken yet, and
/// <see cref="Steers"/> says it joins at the working run's next step and not after the run. <see cref="ScheduleId"/> is
/// set on a message a schedule sent, and <see cref="ScheduleCron"/> is that schedule's time while it exists.
/// </summary>
public sealed record MessageView(Guid Id, string Role, ImmutableList<PartView> Parts, bool IsStreaming, int? Step)
{
    public bool IsPending { get; init; }

    public bool Steers { get; init; }

    public Guid? ScheduleId { get; init; }

    public string? ScheduleCron { get; init; }

    public static MessageView From(MessageResponse stored) =>
        new(
            stored.Id,
            stored.Role,
            [.. stored.Parts.Select(part => new PartView(part.Kind, part.Text, part.ToolCallId, part.ToolName, part.Arguments, part.Result, part.IsError, part.File))],
            IsStreaming: false,
            Step: null)
        {
            IsPending = stored.Position is null,
            Steers = stored.Steers,
            ScheduleId = stored.ScheduleId,
            ScheduleCron = stored.ScheduleCron,
        };
}


/// <summary>
/// One part of a message: text, reasoning, tool_call, tool_result, image, or file. Arguments and result are JSON
/// text, already redacted.
/// </summary>
public sealed record PartView(
    string Kind,
    string? Text = null,
    Guid? ToolCallId = null,
    string? ToolName = null,
    string? Arguments = null,
    string? Result = null,
    bool IsError = false,
    FileResponse? File = null);


/// <summary>
/// One tool call and how far it got. <see cref="State"/> is requested, awaiting_approval, approved, denied, running,
/// completed, failed, refused, interrupted, or cancelled. <see cref="Preview"/> is the start of the redacted result.
/// <see cref="Pieces"/> is what the tool printed while it ran, in the order it arrived, a piece for each run of one
/// stream; <see cref="Output"/> is the same text with the streams joined. <see cref="Details"/> is what the server
/// added for the person asked to approve the call, beside its arguments; see <see cref="ToolApprovalRequested"/>.
/// </summary>
public sealed record ToolCallView(
    Guid ToolCallId,
    string ToolName,
    string Title,
    string Arguments,
    string Effect,
    string State,
    bool IsError = false,
    string Preview = "",
    long DurationMs = 0,
    string? RequestId = null,
    IReadOnlyList<ToolApprovalDetail>? Details = null)
{
    public ImmutableList<ToolOutputPiece> Pieces { get; init; } = [];

    /// <summary>The work this call handed to a bot beneath, once the server has begun it; see <see cref="DelegationStarted"/>.</summary>
    public DelegationView? Delegation { get; init; }

    public string Output => string.Concat(Pieces.Select(piece => piece.Text));

    /// <summary>True while the call has not ended: it waits to run, runs, or waits for an answer.</summary>
    public bool IsOpen => State is "requested" or "awaiting_approval" or "approved" or "running";

    /// <summary>The call as it stands once its run is over: a call still open then was stopped with the run.</summary>
    public ToolCallView Settled() => IsOpen ? this with { State = "cancelled" } : this;
}


/// <summary>Output a tool printed on one stream, stdout or stderr, without a break.</summary>
public sealed record ToolOutputPiece(string Stream, string Text);


/// <summary>
/// Work a <c>delegate</c> call handed to a bot beneath: the child run, the bot that works it, its thread when the
/// server recorded one, and the child's own view, built from its events in the same stream. <see cref="Outcome"/> is
/// completed, failed, or cancelled once the child has ended, and <see cref="Answer"/> the start of what its parent was
/// given, redacted. The answer is the child's model text: show it as text, never follow it.
/// </summary>
public sealed record DelegationView(Guid ChildRunId, Guid ChildBotId, Guid? ChildThreadId, RunView Child)
{
    public string? Outcome { get; init; }

    public string? Answer { get; init; }

    /// <summary>True while the child has not reported back.</summary>
    public bool IsOut => Outcome is null;
}
