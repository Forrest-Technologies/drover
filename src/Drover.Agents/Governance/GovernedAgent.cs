namespace Drover.Agents.Governance;

/// <summary>
/// A bot as a member of a team or as a tool of another bot. Every invocation, from whichever orchestration builder
/// or function makes it, goes through the governor first: it checks the delegation and begins the child run, and
/// only then does the bot work. What the child came to goes back through the governor too, which turns it into the
/// parent's result, or stops the parent with it. <see cref="Result"/> is that result once the agent has run.
/// </summary>
public sealed class GovernedAgent(AIAgent innerAgent, IRunGovernor governor, DelegationRequest delegation)
    : DelegatingAIAgent(innerAgent)
{
    #region Properties

    /// <summary>The parent's result, as the governor made it from what the child came to. Null until the agent has run.</summary>
    public ToolOutcome? Result { get; private set; }

    #endregion

    #region Protected Methods

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var child = await governor.BeginChildRun(delegation, cancellationToken);

        if (child.Refusal is { } refused)
        {
            Result = refused;
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, refused.Result.GetRawText()));
        }

        AgentResponse response;

        try
        {
            response = await InnerAgent.RunAsync(messages, session, ChildRunAgent.With(child, options), cancellationToken);
        }
        catch (Exception stopped)
        {
            await Complete(child, new ChildRunReport(stopped is OperationCanceledException ? ChildRunOutcome.Cancelled : ChildRunOutcome.Failed), cancellationToken);
            throw;
        }

        await Complete(child, ChildRunAgent.ReportOf(response), cancellationToken);
        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var child = await governor.BeginChildRun(delegation, cancellationToken);

        if (child.Refusal is { } refused)
        {
            Result = refused;
            yield return new AgentResponseUpdate(ChatRole.Assistant, refused.Result.GetRawText());
            yield break;
        }

        List<AgentResponseUpdate> seen = [];
        await using var updates = InnerAgent.RunStreamingAsync(messages, session, ChildRunAgent.With(child, options), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (await Next(child, updates, cancellationToken))
        {
            seen.Add(updates.Current);
            yield return updates.Current;
        }

        await Complete(child, ChildRunAgent.ReportOf(seen), cancellationToken);
    }

    #endregion

    #region Private Methods

    /// <summary>The inner agent's next update. When it stops with an exception, the child is completed first.</summary>
    private async Task<bool> Next(ChildRun child, IAsyncEnumerator<AgentResponseUpdate> updates, CancellationToken cancellationToken)
    {
        try
        {
            return await updates.MoveNextAsync();
        }
        catch (Exception stopped)
        {
            await Complete(child, new ChildRunReport(stopped is OperationCanceledException ? ChildRunOutcome.Cancelled : ChildRunOutcome.Failed), cancellationToken);
            throw;
        }
    }

    private async Task Complete(ChildRun child, ChildRunReport report, CancellationToken cancellationToken) =>
        Result = await governor.CompleteChildRun(child, report, cancellationToken);

    #endregion
}
