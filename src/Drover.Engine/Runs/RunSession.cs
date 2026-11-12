namespace Drover.Engine.Runs;

/// <summary>The run stopped itself at a limit. <see cref="Reason"/> is the status reason the run ends with.</summary>
public sealed class RunLimitException(string reason, string message) : Exception(message)
{
    public string Reason => reason;
}

/// <summary>A catalog tool as one run sees it: the row, the effective grant at the start, and the name the model uses.</summary>
public sealed record RunTool(Tool Tool, GrantEffect Effect, string ModelName, BuiltinTool? Builtin);

/// <summary>
/// The state of one execution of one run, shared by its governor, its model client, and the executor: which step it
/// is on, which tool calls are in flight, and the one writer of its events. It lives from claim to finish; nothing
/// in it is needed to continue after a restart, because everything that matters is in the database.
/// </summary>
public sealed class RunSession(
    Run run,
    ChatThread thread,
    string workerId,
    RunTenant tenant,
    RunEventWriter writer,
    IReadOnlyList<RunTool> tools,
    Redactor redactor,
    RunOptions options,
    TimeProvider time,
    int stepsSoFar,
    IReadOnlyList<Guid> alreadyDelivered) : IAsyncDisposable
{
    #region Private Fields

    private const int PreviewLength = 500;

    private readonly CancellationTokenSource stop = new();

    private readonly ConcurrentDictionary<string, ToolCallState> calls = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, bool> reruns = new();

    private readonly ConcurrentDictionary<Guid, bool> settled = new();

    private readonly SemaphoreSlim announcing = new(1, 1);

    private readonly HashSet<Guid> delivered = [.. alreadyDelivered];

    private int step = stepsSoFar;

    private StepChanged? openStep;

    private StepChanged? lastStep;

    private StepChanged? unannounced;

    private RunUsage usage = run.Usage;

    #endregion

    #region Properties

    public Guid RunId => run.Id;

    public Guid RootRunId => run.RootRunId;

    public Guid BotId => run.BotId;

    public int Attempt => run.Attempt;

    /// <summary>This execution's claim. Every write it makes is checked against it.</summary>
    public RunFence Fence { get; } = new(workerId, run.Attempt);

    public ChatThread Thread => thread;

    public RunTenant Tenant => tenant;

    public RunEventWriter Writer => writer;

    public Redactor Redactor => redactor;

    public RunUsage Usage => usage;

    /// <summary>Cancelled when a person asked the run to stop, or when another worker took it over.</summary>
    public CancellationToken Stopped => stop.Token;

    public bool CancelledByUser { get; private set; }

    public bool LeaseLost { get; private set; }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync();
        announcing.Dispose();
    }

    #endregion

    #region Stopping

    public void CancelByUser()
    {
        CancelledByUser = true;
        Stop();
    }

    public void LoseLease()
    {
        LeaseLost = true;
        Stop();
    }

    #endregion

    #region Steps

    /// <summary>
    /// Starts a model call. The step's message id is chosen here, before the call, so the deltas, the stored message,
    /// and <c>message.completed</c> all carry the same id.
    /// </summary>
    public async Task<Guid> BeginStep()
    {
        await AnnounceStoredMessage();

        if (step >= options.StepLimit)
        {
            throw new RunLimitException(
                RunReasons.StepLimit,
                $"The run stopped after {options.StepLimit} model calls, the most one run may make.");
        }

        var started = new StepChanged(++step, Guid.CreateVersion7());
        openStep = started;
        lastStep = started;
        await writer.Append(RunId, RunEventTypes.StepStarted, started);
        return started.MessageId;
    }

    /// <summary>
    /// The model call finished. Its message is stored a moment later, and announced when it is. The runtime stores
    /// that message itself, so the claim is checked first: an execution whose run was taken over stops here and
    /// stores nothing.
    /// </summary>
    public async Task EndStep()
    {
        var lease = await tenant.Transact(
            services => services.GetRequiredService<RunRepository>().ReadLease(RunId, Fence, CancellationToken.None),
            CancellationToken.None);

        if (!lease.Held)
        {
            LoseLease();
            throw new OperationCanceledException($"Run {RunId} was taken over during a model call.");
        }

        unannounced = openStep;
        openStep = null;
    }

    public Task Delta(string kind, string text) =>
        lastStep is { } current ? writer.Delta(RunId, current.MessageId, kind, text) : Task.CompletedTask;

    /// <summary>
    /// Emits <c>message.completed</c> for the last model call's message, once it is in the thread. The runtime stores
    /// it right after the call returns and before any tool runs, so this is asked before a tool is decided, before
    /// the next step, and when the run ends.
    /// </summary>
    public async Task AnnounceStoredMessage()
    {
        await announcing.WaitAsync();

        try
        {
            if (unannounced is not { } pending)
            {
                return;
            }

            var (stored, taken) = await tenant.Transact(
                async services =>
                {
                    var messages = services.GetRequiredService<MessageRepository>();
                    return (
                        await messages.FindMessage(pending.MessageId, CancellationToken.None),
                        await messages.ListDeliveredMessages(thread.Id, RunId, CancellationToken.None));
                },
                CancellationToken.None);
            unannounced = null;

            // A model call that produced nothing to store has no message; its step is closed as discarded instead.
            // Messages that steered the call were stored just before its reply, so they are announced before it.
            var closed = stored is null
                ? RunEventWriter.Event(RunId, RunEventTypes.StepDiscarded, pending)
                : RunEventWriter.Event(RunId, RunEventTypes.MessageCompleted, new MessageCompleted(stored.ToResponse()));
            await writer.Write(null, [.. Delivered(taken), closed]);
        }
        finally
        {
            announcing.Release();
        }
    }

    /// <summary>
    /// <c>message.delivered</c> for each message this run took that the log has not announced yet: a pending message
    /// now has its place in the conversation.
    /// </summary>
    public IReadOnlyList<NewRunEvent> Delivered(IReadOnlyList<Message> taken) =>
    [
        .. taken
            .Where(message => delivered.Add(message.Id))
            .Select(message => RunEventWriter.Event(RunId, RunEventTypes.MessageDelivered, new MessageDelivered(message.ToResponse()))),
    ];

    #endregion

    #region Tools

    public RunTool? FindTool(Guid toolId) => tools.FirstOrDefault(tool => tool.Tool.Id == toolId);

    public RunTool? FindToolByModelName(string name) =>
        tools.FirstOrDefault(tool => string.Equals(tool.ModelName, name, StringComparison.Ordinal));

    public void MarkRefused(string callId) => Call(callId).Refused = true;

    /// <summary>A read-only call a restart cut off, which the runtime is about to run again.</summary>
    public void ExpectRerun(Guid toolCallId) => reruns[toolCallId] = true;

    public bool IsRerun(string callId) => reruns.ContainsKey(ToolCallIds.For(RunId, callId));

    /// <summary>A call an earlier execution left open, whose end this one has just written to the log.</summary>
    public void MarkSettled(Guid toolCallId) => settled[toolCallId] = true;

    /// <summary>Emits <c>tool.requested</c> once for a call the model asked for, with its arguments redacted.</summary>
    public async Task RequestTool(string callId, string modelName, JsonElement arguments)
    {
        var call = Call(callId);

        if (!call.TryMarkRequested())
        {
            return;
        }

        var tool = FindToolByModelName(modelName);
        await writer.Append(
            RunId,
            RunEventTypes.ToolRequested,
            new ToolRequested(
                ToolCallIds.For(RunId, callId),
                tool?.Tool.Name ?? modelName,
                tool?.Tool.Title ?? tool?.Tool.Name ?? modelName,
                redactor.Redact(arguments).GetRawText(),
                (tool?.Effect ?? GrantEffect.Deny).ToString().ToLowerInvariant()));
    }

    public async Task StartTool(string callId)
    {
        Call(callId).StartedAt = time.GetTimestamp();
        await writer.Append(RunId, RunEventTypes.ToolStarted, new ToolStarted(ToolCallIds.For(RunId, callId)));
    }

    /// <summary>Emits <c>tool.completed</c> once for a call, whichever of the engine or the runtime reports it first.</summary>
    public async Task CompleteTool(string callId, ToolOutcome outcome, string? outcomeName = null)
    {
        var call = Call(callId);

        if (!call.TryMarkCompleted() || settled.ContainsKey(ToolCallIds.For(RunId, callId)))
        {
            return;
        }

        var name = outcomeName ?? (call.Refused ? "refused" : outcome.IsError ? "failed" : "completed");
        var duration = call.StartedAt is { } started ? (long)time.GetElapsedTime(started).TotalMilliseconds : 0;
        await writer.Append(
            RunId,
            RunEventTypes.ToolCompleted,
            new ToolCompleted(ToolCallIds.For(RunId, callId), name, outcome.IsError, Preview(outcome.Result), duration));
    }

    public string Preview(JsonElement result)
    {
        var text = redactor.Redact(result.ValueKind is JsonValueKind.String ? result.GetString() ?? string.Empty : result.GetRawText());
        return text.Length <= PreviewLength ? text : text[..PreviewLength];
    }

    #endregion

    #region Usage

    public void AddUsage(long inputTokens, long outputTokens, decimal cost) =>
        usage = usage with
        {
            InputTokens = usage.InputTokens + inputTokens,
            OutputTokens = usage.OutputTokens + outputTokens,
            Cost = usage.Cost + cost,
        };

    public int CurrentStep => step;

    #endregion

    #region Finishing

    /// <summary>
    /// The events that close whatever is still open when the run stops: a step whose model call never finished is
    /// discarded, and a tool that started and never returned is reported as cancelled. After them the log says what
    /// the database says, so a client that replays it shows what a reload would.
    /// </summary>
    public IReadOnlyList<NewRunEvent> CloseOpenWork()
    {
        List<NewRunEvent> closing = [];

        if (openStep is { } cut)
        {
            closing.Add(RunEventWriter.Event(RunId, RunEventTypes.StepDiscarded, cut));
            openStep = null;
        }

        foreach (var (callId, call) in calls)
        {
            if (call.StartedAt is not null && call.TryMarkCompleted())
            {
                closing.Add(RunEventWriter.Event(
                    RunId,
                    RunEventTypes.ToolCompleted,
                    new ToolCompleted(ToolCallIds.For(RunId, callId), "cancelled", true, string.Empty, 0)));
            }
        }

        return closing;
    }

    #endregion

    #region Private Methods

    private ToolCallState Call(string callId) => calls.GetOrAdd(callId, _ => new ToolCallState());

    // The source is never disposed: a cancel can arrive from a request just as the run finishes, and cancelling a
    // source with no timer is always safe.
    private void Stop() => stop.Cancel();

    #endregion

    #region Nested Types

    private sealed class ToolCallState
    {
        #region Private Fields

        private int requested;

        private int completed;

        #endregion

        #region Properties

        public bool Refused { get; set; }

        public long? StartedAt { get; set; }

        #endregion

        #region Public Methods

        /// <summary>True the first time only, so the event is written once however many callers race.</summary>
        public bool TryMarkRequested() => Interlocked.Exchange(ref requested, 1) == 0;

        public bool TryMarkCompleted() => Interlocked.Exchange(ref completed, 1) == 0;

        #endregion
    }

    #endregion
}
