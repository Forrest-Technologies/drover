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

    /// <summary>
    /// The value as it may be stored or shown: every secret the run has revealed is replaced. The runtime passes
    /// tool arguments through it before it writes them to a <c>tool_calls</c> row.
    /// </summary>
    JsonElement Redact(JsonElement value);
}
