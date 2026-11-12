namespace Drover.Agents.Runtime;

/// <summary>
/// Runs a bot on the agent framework. It builds the framework agent from the bot's definition, loads the thread's
/// session, runs the agent with streaming, and translates what happens into Drover's events. The framework runs the
/// loop; Drover supplies storage and governance at its extension points.
/// </summary>
public sealed class AgentRuntime(
    IServiceScopeFactory scopes,
    ChatClientResolver resolveChatClient,
    CapabilityResolver resolveCapabilities,
    ILoggerFactory loggerFactory) : IAgentRuntime
{
    #region Private Fields

    private readonly ILogger logger = loggerFactory.CreateLogger<AgentRuntime>();

    #endregion

    #region Interface Implementations

    public async IAsyncEnumerable<AgentEvent> Execute(
        RunExecution execution,
        IRunGovernor governor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var run = await OpenRun(execution, governor, cancellationToken);
        var agent = BuildAgent(run);
        var sessions = new PostgresAgentSessionStore(run, loggerFactory.CreateLogger<PostgresAgentSessionStore>());
        var key = PostgresAgentSessionStore.KeyFor(run);
        var session = await sessions.GetOrCreateSessionAsync(agent, key, cancellationToken);
        var translator = new AgentEventTranslator(run);

        await foreach (var update in agent.RunStreamingAsync(ApprovalMessages(execution), session, cancellationToken: cancellationToken))
        {
            foreach (var agentEvent in await translator.Translate(update, cancellationToken))
            {
                yield return agentEvent;
            }
        }

        await sessions.SaveSessionAsync(agent, key, session, cancellationToken);
        logger.LogInformation("Run {RunId} step finished, waiting: {Waiting}", execution.RunId, translator.Waiting);
        yield return new RunFinished(translator.Waiting ? RunOutcome.Waiting : RunOutcome.Completed);
    }

    #endregion

    #region Telemetry

    /// <summary>The trace source agents report to. Traces leave the server only if the operator sets an endpoint.</summary>
    public const string TelemetrySource = "Drover.Agents";

    #endregion

    #region Building

    private async Task<RunContext> OpenRun(RunExecution execution, IRunGovernor governor, CancellationToken cancellationToken)
    {
        var database = new RunDatabase(scopes, execution.SpaceId, execution.PrincipalUserId);
        var thread = await database.Transact(
            services => services.GetRequiredService<MessageRepository>().GetThread(execution.ThreadId, cancellationToken),
            cancellationToken);

        return new RunContext(execution, governor, database, thread
            ?? throw new InvalidOperationException($"Thread {execution.ThreadId} is not visible to the run's principal."));
    }

    private AIAgent BuildAgent(RunContext run) =>
        resolveChatClient(run.Execution).AsGovernedAgent(
            run,
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = new PostgresChatHistoryProvider(run, loggerFactory.CreateLogger<PostgresChatHistoryProvider>()),
                AIContextProviders = [.. resolveCapabilities(run.Execution.Bot)],
                RequirePerServiceCallChatHistoryPersistence = true,
                AllowConcurrentInvocation = true,
            },
            loggerFactory);

    /// <summary>
    /// The human's answers, as framework approval responses. The framework binds each one by its request id to the
    /// request it recorded in the session, so a response can only approve the call that was actually surfaced.
    /// </summary>
    private static List<ChatMessage> ApprovalMessages(RunExecution execution)
    {
        List<AIContent> responses =
        [
            .. execution.Responses.OfType<ToolApproval>().Select(approval =>
                new ToolApprovalResponseContent(approval.RequestId, approval.Approved, new FunctionCallContent(approval.CallId, string.Empty))
                {
                    Reason = approval.Reason,
                }),
        ];

        return responses.Count == 0 ? [] : [new ChatMessage(ChatRole.User, responses)];
    }

    #endregion
}
