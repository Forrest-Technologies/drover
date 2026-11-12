namespace Drover.Adapters.Conformance.Agents;

/// <summary>
/// The engine's side of a run, as a conformance test sets it up: a fixed decision per tool, every request kept. A new
/// one is made for each host, as the engine would be after a restart; the tools it calls are the shared ones.
/// </summary>
public sealed class ScriptedGovernor(ScriptedTools tools, IReadOnlyDictionary<string, ToolGateDecision>? decisions = null)
    : IRunGovernor
{
    #region Private Fields

    private readonly ConcurrentQueue<ToolCallRequest> decided = new();

    private readonly ConcurrentQueue<DelegationRequest> delegations = new();

    private readonly ConcurrentQueue<(ChildRun Child, ChildRunOutcome Outcome)> completions = new();

    private readonly ConcurrentQueue<ModelUsage> recordedUsage = new();

    #endregion

    #region Properties

    public IReadOnlyList<ToolCallRequest> Decided => [.. decided];

    public IReadOnlyList<DelegationRequest> Delegations => [.. delegations];

    public IReadOnlyList<(ChildRun Child, ChildRunOutcome Outcome)> Completions => [.. completions];

    public IReadOnlyList<ModelUsage> Usage => [.. recordedUsage];

    #endregion

    #region Interface Implementations

    public Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        decided.Enqueue(toolCall);
        return Task.FromResult(decisions?.GetValueOrDefault(toolCall.ToolName) ?? ToolGateDecision.Run);
    }

    public Task<ToolOutcome> InvokeTool(ToolCallRequest toolCall, CancellationToken cancellationToken) =>
        tools.Invoke(toolCall, cancellationToken);

    public Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken)
    {
        delegations.Enqueue(request);
        return Task.FromResult(new ChildRun(Guid.CreateVersion7(), request.AgentName));
    }

    public Task CompleteChildRun(ChildRun child, ChildRunOutcome outcome, CancellationToken cancellationToken)
    {
        completions.Enqueue((child, outcome));
        return Task.CompletedTask;
    }

    public Task RecordUsage(ModelUsage usage, CancellationToken cancellationToken)
    {
        recordedUsage.Enqueue(usage);
        return Task.CompletedTask;
    }

    #endregion
}
