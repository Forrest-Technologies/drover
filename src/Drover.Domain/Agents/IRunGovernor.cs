namespace Drover.Domain.Agents;

/// <summary>
/// The engine's side of a run. The runtime asks it before every tool call, hands it every tool call to execute, and
/// tells it about delegation and usage. The model never decides what it may do: every decision is made here.
/// </summary>
public interface IRunGovernor
{
    Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken);

    Task<ToolOutcome> InvokeTool(ToolCallRequest toolCall, CancellationToken cancellationToken);

    Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken);

    Task CompleteChildRun(ChildRun child, ChildRunOutcome outcome, CancellationToken cancellationToken);

    Task RecordUsage(ModelUsage usage, CancellationToken cancellationToken);
}
