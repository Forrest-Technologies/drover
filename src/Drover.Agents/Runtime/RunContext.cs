namespace Drover.Agents.Runtime;

/// <summary>The state one run's agent shares between its history provider, tool gate, tools, and event translation.</summary>
public sealed class RunContext(RunExecution execution, IRunGovernor governor, RunDatabase database, ChatThread thread)
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, bool> errors = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, TaskCompletionSource> finished = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IReadOnlyList<FunctionCallContent>> batches = new(StringComparer.Ordinal);

    private ShownImage? imageForNextCall;

    #endregion

    #region Properties

    public RunExecution Execution => execution;

    public IRunGovernor Governor => governor;

    public RunDatabase Database => database;

    public ChatThread Thread => thread;

    #endregion

    #region Public Methods

    public ToolDefinition? FindTool(string name) =>
        execution.Tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    public ToolCall NewToolCallRow(Guid id, Guid toolId, string toolName, JsonElement arguments) => new()
    {
        Id = id,
        AudienceUserId = thread.AudienceUserId,
        RunId = execution.RunId,
        ThreadId = thread.Id,
        ToolId = toolId,
        ToolName = toolName,
        Arguments = governor.Redact(arguments),
    };

    public bool IsReadOnly(string toolName) => FindTool(toolName)?.Sensitivity is ToolSensitivity.Read;

    public void RecordOutcome(string callId, bool isError) => errors[callId] = isError;

    public bool WasError(string callId) => errors.TryGetValue(callId, out var isError) && isError;

    /// <summary>Remembers the tool calls one model response asked for, in the order it listed them.</summary>
    public void NoteRequestedCalls(IReadOnlyList<FunctionCallContent> calls)
    {
        foreach (var call in calls)
        {
            batches[call.CallId] = calls;
        }
    }

    /// <summary>The calls listed before this one in the same model response.</summary>
    public IEnumerable<FunctionCallContent> CallsRequestedBefore(string callId) =>
        batches.TryGetValue(callId, out var calls) ? calls.TakeWhile(call => call.CallId != callId) : [];

    /// <summary>Completes when the call with this id has finished, whether it ran, failed, or was refused.</summary>
    public Task Finished(string callId) => Completion(callId).Task;

    public void MarkFinished(string callId) => Completion(callId).TrySetResult();

    /// <summary>
    /// Keeps a tool's picture for the model's next call. Only the latest is kept: an older screenshot shows a screen
    /// that is no longer there.
    /// </summary>
    public void ShowOnNextModelCall(string callId, string toolName, ToolImage image) =>
        Interlocked.Exchange(ref imageForNextCall, new ShownImage(callId, toolName, image));

    /// <summary>The picture kept for the next model call, which only that call sees. Null when there is none.</summary>
    public ShownImage? TakeImageForModelCall() => Interlocked.Exchange(ref imageForNextCall, null);

    #endregion

    #region Private Methods

    private TaskCompletionSource Completion(string callId) =>
        finished.GetOrAdd(callId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    #endregion
}
