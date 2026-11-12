namespace Drover.Domain.Agents;

/// <summary>A bot handing work to another bot, inside a team or as a tool.</summary>
public sealed record DelegationRequest(Guid ParentRunId, string AgentName);


public sealed record ChildRun(Guid RunId, string AgentName);


public enum ChildRunOutcome
{
    Completed,
    Failed,
    Cancelled,
}
