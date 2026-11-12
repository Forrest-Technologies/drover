namespace Drover.Domain.Agents;

/// <summary>
/// Runs one step of a bot's run on the agent framework. The engine calls it and reads the events; it exists so the
/// run engine can be tested with a scripted runtime and no model, not as a hedge against the framework.
/// </summary>
public interface IAgentRuntime
{
    IAsyncEnumerable<AgentEvent> Execute(
        RunExecution execution,
        IRunGovernor governor,
        CancellationToken cancellationToken);
}
