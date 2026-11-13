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

    /// <summary>
    /// Whether the bot has a rule about its replies. When it has, a reply is held back until it is complete and has
    /// passed the rules, so that nothing a rule forbids is shown or stored. A governor with no rules holds nothing.
    /// </summary>
    Task<bool> HoldsReplies(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>
    /// The text a reply may be shown as: unchanged, with matches removed, or replaced by a statement that a rule
    /// withheld it. Called once for each complete piece of text of one model call.
    /// </summary>
    Task<string> RuleOnReply(string text, CancellationToken cancellationToken) => Task.FromResult(text);

    /// <summary>
    /// Checks a person's messages that arrived during the run, before the model sees them. When a rule does not
    /// let one through, the run ends and this throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task RuleOnArrivals(IReadOnlyList<string> texts, CancellationToken cancellationToken) => Task.CompletedTask;
}
