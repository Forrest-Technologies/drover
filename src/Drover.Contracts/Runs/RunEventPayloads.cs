namespace Drover.Contracts.Runs;

/// <summary>Data of every <c>run.*</c> event: the status the run now has, and why.</summary>
public sealed record RunStatusChanged(string Status, string? Reason, RunProblem? Error, int Attempt, UsageTotals? Usage);

/// <summary>
/// Data of <c>step.started</c> and <c>step.discarded</c>. A step is one model call. <see cref="MessageId"/> is the
/// id the step's assistant message will be stored under, and the id its deltas carry. A discarded step's partial
/// output is dropped: the step was cut off and its message was never stored.
/// </summary>
public sealed record StepChanged(int Step, Guid MessageId, long? EstimatedInputTokens = null, bool Shortened = false);

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
/// what an answer must name. <see cref="Details"/> is what the server adds for the person to decide on that the call's
/// arguments do not say, redacted like them; null when the tool adds nothing. <c>crew.add</c> adds
/// <see cref="ModelDetail"/>, the model the new bot will use, and <see cref="BeneathDetail"/>, the name of the bot it
/// will go beneath.
/// </summary>
public sealed record ToolApprovalRequested(Guid ToolCallId, string RequestId, string CallId, IReadOnlyList<ToolApprovalDetail>? Details = null)
{
    public const string ModelDetail = "model";

    public const string BeneathDetail = "beneath";
}

/// <summary>One thing a person is shown about a call that waits for them: a stable name a screen can find it by, and its text.</summary>
public sealed record ToolApprovalDetail(string Name, string Text);

/// <summary>Data of <c>tool.approved</c> and <c>tool.denied</c>: who answered the approval request.</summary>
public sealed record ToolDecided(Guid ToolCallId, Guid DecidedByUserId);

/// <summary>
/// Data of <c>message.delivered</c>: a message sent while the bot was working has joined the conversation, with the
/// position it was given.
/// </summary>
public sealed record MessageDelivered(MessageResponse Message);

public sealed record ToolStarted(Guid ToolCallId);

/// <summary>
/// Data of <c>tool.output</c>: output a running tool produced, redacted, in the order it arrived. <see cref="Stream"/>
/// is "stdout" or "stderr". Pieces are joined as they come, so one event can hold several lines or part of one.
/// </summary>
public sealed record ToolOutput(Guid ToolCallId, string Stream, string Text);

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

/// <summary>
/// One check of a rule written as a sentence. The outcome is "applies", "passes", "unsure", or "failed"; the
/// probability is the one the decision model gave that the rule applies, when it gave one. It never holds the
/// sentence, the checked text, or anything else that was sent.
/// </summary>
public sealed record RuleChecked(Guid RuleId, string Checkpoint, string Outcome, double? Probability, long ElapsedMs, Guid? ToolCallId);

/// <summary>Data of <c>usage.recorded</c>: what one model call used. <see cref="Cost"/> is null when its price is not known.</summary>
public sealed record UsageRecorded(int Step, long Input, long Output, long Cached, decimal? Cost, string Currency);

/// <summary>
/// Data of <c>delegation.started</c>: the parent's tool call, the child run it began, the bot that works it, and the
/// child's thread. Events written before the thread was recorded here have none.
/// </summary>
public sealed record DelegationStarted(Guid ToolCallId, Guid ChildRunId, Guid ChildBotId, Guid? ChildThreadId = null);

/// <summary>
/// Data of <c>delegation.completed</c>: how the child run ended (<c>completed</c>, <c>failed</c>, or <c>cancelled</c>)
/// and the start of the result its parent was given, redacted.
/// </summary>
public sealed record DelegationCompleted(Guid ToolCallId, Guid ChildRunId, Guid ChildBotId, string Outcome, string Summary);


/// <summary>
/// Data of <c>memory_service_unavailable</c>: the configured provider kind and why recall failed, with no message
/// text, fact, or secret.
/// </summary>
public sealed record MemoryServiceUnavailable(string Provider, string Reason);
