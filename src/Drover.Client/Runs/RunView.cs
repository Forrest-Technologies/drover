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

    #endregion
}


/// <summary>
/// One message as a screen shows it. <see cref="IsStreaming"/> is true while its text is still arriving; a streamed
/// message is replaced by the stored one when it completes, and dropped if its step is discarded.
/// <see cref="IsPending"/> is true for a message sent while the bot was working that no run has taken yet, and
/// <see cref="Steers"/> says it joins at the working run's next step and not after the run.
/// </summary>
public sealed record MessageView(Guid Id, string Role, ImmutableList<PartView> Parts, bool IsStreaming, int? Step)
{
    public bool IsPending { get; init; }

    public bool Steers { get; init; }

    public static MessageView From(MessageResponse stored) =>
        new(
            stored.Id,
            stored.Role,
            [.. stored.Parts.Select(part => new PartView(part.Kind, part.Text, part.ToolCallId, part.ToolName, part.Arguments, part.Result, part.IsError))],
            IsStreaming: false,
            Step: null)
        {
            IsPending = stored.Position is null,
            Steers = stored.Steers,
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
    bool IsError = false);


/// <summary>
/// One tool call and how far it got. <see cref="State"/> is requested, awaiting_approval, approved, denied, running,
/// completed, failed, refused, interrupted, or cancelled. <see cref="Preview"/> is the start of the redacted result.
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
    string? RequestId = null);
