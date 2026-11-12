namespace Drover.Agents.Governance;

/// <summary>
/// A bot as a member of a team or as a tool of another bot. Every invocation, from whichever orchestration builder
/// makes it, goes through the governor first: it creates the child run, and only then does the bot work.
/// </summary>
public sealed class GovernedAgent(AIAgent innerAgent, IRunGovernor governor, Guid parentRunId)
    : DelegatingAIAgent(innerAgent)
{
    #region Protected Methods

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var child = await Begin(cancellationToken);
        var outcome = ChildRunOutcome.Failed;

        try
        {
            var response = await InnerAgent.RunAsync(messages, session, options, cancellationToken);
            outcome = ChildRunOutcome.Completed;
            return response;
        }
        catch (OperationCanceledException)
        {
            outcome = ChildRunOutcome.Cancelled;
            throw;
        }
        finally
        {
            await governor.CompleteChildRun(child, outcome, CancellationToken.None);
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var child = await Begin(cancellationToken);
        var outcome = ChildRunOutcome.Failed;

        try
        {
            await foreach (var update in InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken))
            {
                yield return update;
            }

            outcome = ChildRunOutcome.Completed;
        }
        finally
        {
            await governor.CompleteChildRun(child, cancellationToken.IsCancellationRequested ? ChildRunOutcome.Cancelled : outcome, CancellationToken.None);
        }
    }

    #endregion

    #region Private Methods

    private Task<ChildRun> Begin(CancellationToken cancellationToken) =>
        governor.BeginChildRun(new DelegationRequest(parentRunId, Name ?? Id), cancellationToken);

    #endregion
}
