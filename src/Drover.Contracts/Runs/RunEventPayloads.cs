namespace Drover.Contracts.Runs;

/// <summary>Data of every <c>run.*</c> event: the status the run now has, and why.</summary>
public sealed record RunStatusChanged(string Status, string? Reason, RunProblem? Error, int Attempt, UsageTotals? Usage);

/// <summary>
/// Data of <c>step.started</c> and <c>step.discarded</c>. A step is one model call. <see cref="MessageId"/> is the
/// id the step's assistant message will be stored under, and the id its deltas carry. A discarded step's partial
/// output is dropped: the step was cut off and its message was never stored.
/// </summary>
public sealed record StepChanged(int Step, Guid MessageId);

/// <summary>Data of <c>message.delta</c>: one coalesced chunk of a part. <see cref="Kind"/> is text or reasoning.</summary>
public sealed record MessageDelta(Guid MessageId, int PartIndex, string Kind, string Text);

/// <summary>Data of <c>message.completed</c>: the message as stored. It replaces whatever the deltas built.</summary>
public sealed record MessageCompleted(MessageResponse Message);

/// <summary>
/// Data of <c>tool.requested</c>: the model asked for a call. <see cref="Arguments"/> is the redacted arguments as
/// JSON text. <see cref="Effect"/> is allow or ask.
/// </summary>
public sealed record ToolRequested(Guid ToolCallId, string ToolName, string Title, string Arguments, string Effect);

/// <summary>
/// Data of <c>tool.approval_requested</c>: the call waits for a person. The request id and the model's call id are
/// what an answer must name.
/// </summary>
public sealed record ToolApprovalRequested(Guid ToolCallId, string RequestId, string CallId);

public sealed record ToolStarted(Guid ToolCallId);

/// <summary>
/// Data of <c>tool.completed</c>. <see cref="Outcome"/> is completed, failed, refused, interrupted, or cancelled.
/// <see cref="Preview"/> is the start of the redacted result.
/// </summary>
public sealed record ToolCompleted(Guid ToolCallId, string Outcome, bool IsError, string Preview, long DurationMs);

/// <summary>Data of <c>usage.recorded</c>: what one model call used.</summary>
public sealed record UsageRecorded(int Step, long Input, long Output, long Cached, decimal Cost, string Currency);
