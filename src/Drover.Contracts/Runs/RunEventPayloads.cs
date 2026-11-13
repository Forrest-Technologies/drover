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

/// <summary>Data of <c>tool.approved</c> and <c>tool.denied</c>: who answered the approval request.</summary>
public sealed record ToolDecided(Guid ToolCallId, Guid DecidedByUserId);

/// <summary>
/// Data of <c>message.delivered</c>: a message sent while the bot was working has joined the conversation, with the
/// position it was given.
/// </summary>
public sealed record MessageDelivered(MessageResponse Message);

public sealed record ToolStarted(Guid ToolCallId);

/// <summary>
/// Data of <c>tool.completed</c>. <see cref="Outcome"/> is completed, failed, refused, interrupted, or cancelled.
/// <see cref="Preview"/> is the start of the redacted result.
/// </summary>
public sealed record ToolCompleted(Guid ToolCallId, string Outcome, bool IsError, string Preview, long DurationMs);

/// <summary>
/// Data of <c>rule.fired</c>: a rule applied and did what it says. The checkpoint is "before_tool", "after_tool",
/// "before_reply", or "message_arrives"; the effect is "redact", "ask", "block", or "stop". <see cref="Why"/> says
/// which conditions held, in words, and never holds the text that matched.
/// </summary>
public sealed record RuleFired(Guid RuleId, string RuleName, string Checkpoint, string Effect, string Why, Guid? ToolCallId);

/// <summary>Data of <c>usage.recorded</c>: what one model call used.</summary>
public sealed record UsageRecorded(int Step, long Input, long Output, long Cached, decimal Cost, string Currency);
