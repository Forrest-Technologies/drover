namespace Drover.Agents.Governance;

/// <summary>
/// A bot beneath another, as the agent its parent's <c>delegate</c> call runs. Its run is the child run the governor
/// began: the engine executes it as a run of its own, with the child's own grants, history, session, and events, and
/// builds its framework agent the way every bot's is built. The messages it is handed are not used: the task is the
/// first message of the child's own thread, stored when the child run began, so a restart finds it there.
/// </summary>
public sealed class ChildRunAgent(IRunGovernor governor, string name) : AIAgent
{
    #region Private Fields

    private const string ChildRunKey = "drover.child_run";

    private const string ReportKey = "drover.child_run_report";

    #endregion

    #region Properties

    public override string Name => name;

    #endregion

    #region Public Methods

    /// <summary>The run options that carry the child run a <see cref="GovernedAgent"/> began to the agent it wraps.</summary>
    public static AgentRunOptions With(ChildRun child, AgentRunOptions? options)
    {
        var carrying = options?.Clone() ?? new AgentRunOptions();
        carrying.AdditionalProperties ??= [];
        carrying.AdditionalProperties[ChildRunKey] = child;
        return carrying;
    }

    /// <summary>What a child run came to, from the response of the agent that ran it. Any other agent completed.</summary>
    public static ChildRunReport ReportOf(AgentResponse response) =>
        response.AdditionalProperties?.TryGetValue(ReportKey, out var report) == true && report is ChildRunReport found
            ? found
            : new ChildRunReport(ChildRunOutcome.Completed, response.Text);

    public static ChildRunReport ReportOf(IReadOnlyList<AgentResponseUpdate> updates) =>
        updates.Select(update => update.AdditionalProperties?.TryGetValue(ReportKey, out var report) == true ? report as ChildRunReport : null)
            .OfType<ChildRunReport>()
            .LastOrDefault()
        ?? new ChildRunReport(ChildRunOutcome.Completed, string.Concat(updates.Select(update => update.Text)));

    #endregion

    #region Protected Methods

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var report = await governor.RunChildRun(ChildOf(options), cancellationToken);
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, report.Answer ?? string.Empty))
        {
            AdditionalProperties = new() { [ReportKey] = report },
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var report = await governor.RunChildRun(ChildOf(options), cancellationToken);
        yield return new AgentResponseUpdate(ChatRole.Assistant, report.Answer ?? string.Empty)
        {
            AdditionalProperties = new() { [ReportKey] = report },
        };
    }

    // The child run keeps its framework session in its own thread's row. This agent holds none of its own.
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new ChildRunSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(JsonSerializer.SerializeToElement(new { }));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new ChildRunSession());

    #endregion

    #region Private Methods

    private static ChildRun ChildOf(AgentRunOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(ChildRunKey, out var child) == true && child is ChildRun found
            ? found
            : throw new InvalidOperationException("A child run agent runs only inside a governed agent, which begins its child run.");

    #endregion

    #region Nested Types

    private sealed class ChildRunSession : AgentSession;

    #endregion
}
