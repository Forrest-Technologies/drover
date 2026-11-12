namespace Drover.Domain.Agents;

/// <summary>What happened in a run, in Drover's own shape. The engine turns these into run events.</summary>
public abstract record AgentEvent;

public sealed record TextDelta(string Text) : AgentEvent;

public sealed record ReasoningDelta(string Text) : AgentEvent;

/// <summary>The model asked for a tool call. Whether it runs is the gate's decision.</summary>
public sealed record ToolCallRequested(string CallId, string ToolName, JsonElement Arguments) : AgentEvent;

public sealed record ToolCallFinished(string CallId, bool IsError, JsonElement Result) : AgentEvent;

/// <summary>A call needs a human's answer. The run ends <see cref="RunOutcome.Waiting"/> and resumes with it.</summary>
public sealed record ApprovalRequested(string RequestId, string CallId, string ToolName, JsonElement Arguments)
    : AgentEvent;

public sealed record RunFinished(RunOutcome Outcome) : AgentEvent;


public enum RunOutcome
{
    Completed,
    Waiting,
}
