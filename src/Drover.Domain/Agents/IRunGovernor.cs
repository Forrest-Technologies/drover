namespace Drover.Domain.Agents;

/// <summary>
/// The engine's side of a run. The runtime asks it before every tool call, hands it every tool call to execute, and
/// tells it about delegation and usage. The model never decides what it may do: every decision is made here.
/// </summary>
public interface IRunGovernor
{
    Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken);

    Task<ToolOutcome> InvokeTool(ToolCallRequest toolCall, CancellationToken cancellationToken);

    /// <summary>
    /// Begins a child run for the request, after checking the grant, the target, the run's ancestors for a loop, and
    /// the tree's budget, in one transaction. Asked again for the same tool call it returns the same child run.
    /// </summary>
    Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a child run this governor began, as its own run with its own grants, history, and events, until it
    /// ends or waits for a person. A child that ended already reports how it ended and does not run again.
    /// </summary>
    Task<ChildRunReport> RunChildRun(ChildRun child, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a child run's report into its parent and returns the parent's tool result. When the parent must stop
    /// here (the child waits for a person and the tree pauses, or the parent itself was cancelled) it throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ToolOutcome> CompleteChildRun(ChildRun child, ChildRunReport report, CancellationToken cancellationToken);

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
    /// What a tool call's arguments are after the bot's rules have removed what they remove, asked as soon as the
    /// model has written the call: before it is shown, stored, or put in front of a person. The gate decides about
    /// the call later; this only makes sure nothing downstream ever holds the text a rule removes.
    /// </summary>
    Task<JsonElement> RuleOnCallArguments(string callId, string toolName, JsonElement arguments, CancellationToken cancellationToken) => Task.FromResult(arguments);

    /// <summary>
    /// Checks a person's messages that arrived during the run, before the model sees them. When a rule does not
    /// let one through, the run ends and this throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task RuleOnArrivals(IReadOnlyList<Message> messages, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// What the model is given for each of these attached files, by file id, read as the run's person. A picture the
    /// model reads is read each time; anything copied into the sandbox is copied once a run. A file left out of the
    /// answer is given to the model as a line saying it could not be opened.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, FileForModel>> OpenFiles(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, FileForModel>>(new Dictionary<Guid, FileForModel>());
}
