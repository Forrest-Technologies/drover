namespace Drover.Engine.Runs;

/// <summary>The run stopped itself at a limit. <see cref="Reason"/> is the status reason the run ends with.</summary>
public sealed class RunLimitException(string reason, string message, string kind, long used, long cap) : Exception(message)
{
    #region Properties

    public string Reason => reason;

    public string Kind => kind;

    public long Used => used;

    public long Cap => cap;

    #endregion
}

/// <summary>A catalog tool as one run sees it: the row, the effective grant at the start, and the name the model uses.</summary>
public sealed record RunTool(Tool Tool, GrantEffect Effect, string ModelName, BuiltinTool? Builtin, ToolConnection? Connection = null)
{
    /// <summary>Drover knows this tool only reads: it is built in and marked so. A server's word for it is not enough.</summary>
    public bool IsKnownToOnlyRead => Builtin is not null && Tool.Sensitivity is ToolSensitivity.Read;

    /// <summary>The built-in <c>delegate</c> tool, whose calls run a bot beneath this one as a child run.</summary>
    public bool IsDelegation => Builtin is not null && Builtin.Name == DelegateTool.Name;

    /// <summary>
    /// A call cut off by a restart is asked again: it only reads, it resolves to the same child run, or Drover knows
    /// running it twice does what running it once did.
    /// </summary>
    public bool MayRunAgain => IsKnownToOnlyRead || IsDelegation || Builtin is { IsIdempotent: true };
}

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
    RunCeilings limits,
    int stepsSoFar,
    IReadOnlyList<Guid> alreadyDelivered,
    RunSession? parent = null) : IAsyncDisposable
{
    #region Private Fields

    private const int PreviewLength = 500;

    private readonly CancellationTokenSource stop = new();

    private readonly ConcurrentDictionary<string, ToolCallState> calls = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, bool> reruns = new();

    private readonly ConcurrentDictionary<Guid, bool> settled = new();

    private readonly ConcurrentDictionary<Guid, bool> requestedBefore = new();

    private readonly SemaphoreSlim announcing = new(1, 1);

    private readonly HashSet<Guid> delivered = [.. alreadyDelivered];

    private readonly ConcurrentDictionary<string, bool> waitingOnChildren = new(StringComparer.Ordinal);

    private int step = stepsSoFar;

    /// <summary>Model calls tools made for this execution, such as reading the screen. They count against the step limit.</summary>
    private int toolModelCalls;

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

    /// <summary>
    /// This execution's claim. Every write it makes is checked against it. A child run has no claim of its own: the
    /// root's lease covers the tree, so a child's writes are checked against the root's claim.
    /// </summary>
    public RunFence Fence { get; } = parent?.Fence ?? new(workerId, run.Attempt);

    /// <summary>The run whose lease covers this one: the run itself for a root, the tree's root for a child.</summary>
    public Guid LeaseRunId => parent is null ? run.Id : run.RootRunId;

    /// <summary>The session of the run that delegated to this one, executing in the same worker. Null for a root.</summary>
    public RunSession? Parent => parent;

    public Guid? ParentToolCallId => run.ParentToolCallId;

    public int Depth => run.Depth;

    /// <summary>A delegation beneath this run waits for a person, so this run stops here and waits with it.</summary>
    public bool PausedForChild { get; private set; }

    /// <summary>The run asked a person to take over its bot's screen in this execution, so it waits for them there.</summary>
    public bool WaitsForAPersonAtTheScreen { get; private set; }

    public ChatThread Thread => thread;

    public RunTenant Tenant => tenant;

    public RunEventWriter Writer => writer;

    public Redactor Redactor => redactor;

    public RunUsage Usage => usage;

    /// <summary>Cancelled when a person asked the run to stop, or when another worker took it over.</summary>
    public CancellationToken Stopped => stop.Token;

    public bool CancelledByUser { get; private set; }

    /// <summary>Set when a rule ended the run: the reason code and the sentence the run fails with.</summary>
    public (string Reason, string Message)? EndedByRule { get; private set; }

    /// <summary>The bot's rules as they were when the agent was last built, which decides the tools that must ask.</summary>
    public IReadOnlyList<ActiveRule> RulesAtBuild { get; set; } = [];

    /// <summary>
    /// How many tool calls the model has asked for in the whole run: the ones its log holds from executions before
    /// this one, a wait for a person or a restart included, and the ones made since.
    /// </summary>
    public int ToolCallCount => requestedBefore.Keys.Concat(calls.Keys.Select(callId => ToolCallIds.For(RunId, callId))).Distinct().Count();

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>
    /// What the rules said about each tool call when the model wrote it, on the arguments as written. The gate
    /// decides from this, because by then the arguments hold only what the rules left.
    /// </summary>
    public ConcurrentDictionary<string, RuledCall> RuledCalls { get; } = new(StringComparer.Ordinal);

    public bool LeaseLost { get; private set; }

    public RunOptions Options => options;

    /// <summary>
    /// The run itself has received a result from an untrusted source. It is a record and nothing more: no tool asks
    /// or is refused because of it. It stays so until the run ends, and a note the run writes carries it.
    /// </summary>
    public bool Tainted { get; private set; } = run.Tainted;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync();
        announcing.Dispose();
    }

    #endregion

    #region Stopping

    /// <summary>Ends the run because a rule says so. The first rule to end it is the one the run reports.</summary>
    public void EndByRule(string reason, string message)
    {
        EndedByRule ??= (reason, message);
        stop.Cancel();
    }

    /// <summary>
    /// True when a rule asks a person before this tool is used, going by the rule's conditions about the tool alone.
    /// The framework can stop for approval only on tools marked for it when the agent is built, before any argument
    /// is known, so a rule that asks only for some arguments makes every call of the tools it could apply to ask. A rule
    /// written as a sentence marks no tool yet: until it can pause a tool, a person it would ask is asked by refusing
    /// the call and saying approval is needed.
    /// </summary>
    public bool RuleAsks(RunTool tool) =>
        RulesAtBuild.Any(rule =>
            rule is { Checkpoint: RuleCheckpoint.BeforeTool, Effect: RuleEffect.Ask, Judged: null }
            && rule.Conditions.All(condition => condition.Kind switch
            {
                RuleConditionKind.ToolIs => condition.Tools.Contains(tool.Tool.Name, StringComparer.Ordinal),
                RuleConditionKind.ToolKind => condition.ToolKinds.Contains(tool.Tool.Sensitivity),
                _ => true,
            }));

    /// <summary>
    /// A child run of this one waits for a person. This run stops where it is and waits too: its call to the child
    /// stays open, and is asked again when the answer arrives, which takes it to the same child run.
    /// </summary>
    public void PauseForChild(string callId)
    {
        waitingOnChildren[callId] = true;
        PausedForChild = true;
        Stop();
    }

    /// <summary>
    /// The model asked for a call that waits for a person. When it is the call that asks a person to take over the
    /// screen, the run waits with the screen held, so no bot that shares it acts there until it is handed back.
    /// </summary>
    public void NoteWaitFor(string modelName)
    {
        if (FindToolByModelName(modelName)?.Builtin?.Name is ComputerTools.HandoverName)
        {
            WaitsForAPersonAtTheScreen = true;
        }
    }

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
    public async Task<Guid> BeginStep(long estimatedInputTokens, bool shortened)
    {
        await AnnounceStoredMessage();

        var callsMade = step + Volatile.Read(ref toolModelCalls);
        if (limits.MaxSteps != int.MaxValue && callsMade >= limits.MaxSteps)
        {
            throw new RunLimitException(
                RunReasons.StepLimit,
                $"This work stopped after {limits.MaxSteps} model calls, the most it may make. The limit is in the bot's settings, under Limits.",
                FailureLimits.ModelCalls,
                callsMade,
                limits.MaxSteps);
        }

        if (!limits.TokensUnlimited && TokensUsed >= limits.MaxTokens)
        {
            throw new RunLimitException(
                RunReasons.TokenLimit,
                $"This work stopped after it used {TokensUsed:N0} tokens. It may use {limits.MaxTokens:N0}. The limit is in the bot's settings, under Limits.",
                FailureLimits.Tokens,
                TokensUsed,
                limits.MaxTokens);
        }

        var started = new StepChanged(++step, Guid.CreateVersion7(), estimatedInputTokens, shortened);
        openStep = started;
        lastStep = started;
        await writer.Append(RunId, RunEventTypes.StepStarted, started);
        return started.MessageId;
    }

    /// <summary>
    /// A tool is about to call a model for this run, as the screen reader does. The call counts against the same limit
    /// as the run's own model calls, and is refused in words once the run has made as many as it may.
    /// </summary>
    public Task BeginToolModelCall()
    {
        if (!limits.TokensUnlimited && TokensUsed >= limits.MaxTokens)
        {
            throw new BuiltinToolException($"This run has used {TokensUsed:N0} tokens, and it may use {limits.MaxTokens:N0}, so no other model is called for it.");
        }

        if (limits.MaxSteps != int.MaxValue && step + Interlocked.Increment(ref toolModelCalls) > limits.MaxSteps)
        {
            Interlocked.Decrement(ref toolModelCalls);
            throw new BuiltinToolException($"This run has made {limits.MaxSteps} model calls, the most one run may make, so no other model is called for it.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The model call finished. Its message is stored a moment later, and announced when it is. The runtime stores
    /// that message itself, so the claim is checked first: an execution whose run was taken over stops here and
    /// stores nothing.
    /// </summary>
    public async Task EndStep()
    {
        var lease = await tenant.Transact(
            services => services.GetRequiredService<RunRepository>().ReadLease(LeaseRunId, Fence, CancellationToken.None),
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
                : RunEventWriter.Event(RunId, RunEventTypes.MessageCompleted, new MessageCompleted(stored.ToResponse(runPrincipalUserId: Tenant.UserId)));
            await writer.Write(null, [.. Delivered(taken), closed]);
        }
        finally
        {
            announcing.Release();
        }
    }

    /// <summary>
    /// <c>message.completed</c> again for a reply a tool changed after it was announced, as stored now: a file the bot
    /// shared is added to the reply that asked for it. A client replaces the message it holds by id.
    /// </summary>
    public async Task AnnounceChangedMessage(Message stored)
    {
        await announcing.WaitAsync();

        try
        {
            await writer.Write(null, [RunEventWriter.Event(RunId, RunEventTypes.MessageCompleted, new MessageCompleted(stored.ToResponse(runPrincipalUserId: Tenant.UserId)))]);
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

    #region Grants And Taint

    /// <summary>
    /// What the bot may do with the tool right now: the grant, except that "allow" on a built-in tool that always asks
    /// behaves as "ask", so it never runs on allow alone. What the run read changes nothing here: "allow" runs.
    /// </summary>
    public GrantEffect EffectNow(RunTool tool, GrantEffect granted) =>
        granted is GrantEffect.Allow && tool.Builtin is { AlwaysAsks: true } ? GrantEffect.Ask : granted;

    /// <summary>Records that the run read untrusted text, on the row as well, so a later execution of the run still shows it.</summary>
    public async Task Taint()
    {
        if (Tainted)
        {
            return;
        }

        Tainted = true;
        await tenant.Transact(
            async services =>
            {
                if (await services.GetRequiredService<RunRepository>().GetRunForUpdate(RunId, CancellationToken.None) is { } row)
                {
                    row.Tainted = true;
                }
            },
            CancellationToken.None);
    }

    #endregion

    #region Tools

    public RunTool? FindTool(Guid toolId) => tools.FirstOrDefault(tool => tool.Tool.Id == toolId);

    public RunTool? FindToolByModelName(string name) =>
        tools.FirstOrDefault(tool => string.Equals(tool.ModelName, name, StringComparison.Ordinal));

    public void MarkRefused(string callId) => Call(callId).Refused = true;

    /// <summary>A read-only call a restart cut off, which the runtime is about to run again.</summary>
    public void ExpectRerun(Guid toolCallId) => reruns[toolCallId] = true;

    public bool IsRerun(string callId) => reruns.ContainsKey(ToolCallIds.For(RunId, callId));

    /// <summary>A cut-off call that is to be asked again and has not started again in this execution yet.</summary>
    public bool AwaitsRerun(string callId) => IsRerun(callId) && Call(callId).StartedAt is null;

    /// <summary>A call the log already holds <c>tool.requested</c> for, from an earlier execution.</summary>
    /// <summary>Counts a call the model has written, before anything else is known about it.</summary>
    public void NoteCall(string callId) => Call(callId);

    public void MarkRequested(Guid toolCallId) => requestedBefore[toolCallId] = true;

    /// <summary>A call whose end is in the log already: an earlier execution wrote it, or this one just did.</summary>
    public void MarkSettled(Guid toolCallId) => settled[toolCallId] = true;

    /// <summary>Emits <c>tool.requested</c> once for a call the model asked for, with its arguments redacted.</summary>
    public async Task RequestTool(string callId, string modelName, JsonElement arguments)
    {
        var call = Call(callId);

        if (!call.TryMarkRequested() || requestedBefore.ContainsKey(ToolCallIds.For(RunId, callId)))
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
                (tool is null ? GrantEffect.Deny : EffectNow(tool, tool.Effect)).ToString().ToLowerInvariant()));
    }

    public async Task StartTool(string callId)
    {
        Call(callId).StartedAt = time.GetTimestamp();
        await writer.Append(RunId, RunEventTypes.ToolStarted, new ToolStarted(ToolCallIds.For(RunId, callId)));
    }

    /// <summary>
    /// Streams a piece of a running tool's output into the log as <c>tool.output</c>, redacted. A call streams at most
    /// <see cref="RunOptions.MaxToolOutputCharacters"/>; past that the log says once that the rest is left out.
    /// </summary>
    public async Task ToolOutput(string callId, ToolOutputChunk chunk)
    {
        var call = Call(callId);
        var text = call.TakeOutput(redactor.Redact(chunk.Text), options.MaxToolOutputCharacters);

        if (text.Length > 0)
        {
            await writer.Output(RunId, ToolCallIds.For(RunId, callId), chunk.Stream, text);
        }
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
            InputTokens = usage.InputTokens + Math.Max(0, inputTokens),
            OutputTokens = usage.OutputTokens + Math.Max(0, outputTokens),
            Cost = usage.Cost + cost,
        };

    public int CurrentStep => step;

    /// <summary>Counts one rule check by a decision model. It is stored with the run's usage when the check is recorded.</summary>
    public void CountJudgedCheck() => usage = usage with { JudgedChecks = usage.JudgedChecks + 1 };

    /// <summary>
    /// Records one model call against the run: the run's own, one a tool made for it, such as reading the screen, or
    /// a rule check. The record names the model that answered, so a call by another model is told apart from the
    /// bot's own. It is not a step.
    /// </summary>
    public async Task RecordModelUsage(ToolModelUsage reported)
    {
        var used = RunGovernor.NonNegative(reported);
        var cost = RunGovernor.Cost(used);
        AddUsage(used.InputTokens, used.OutputTokens, cost ?? 0);
        var recorded = new UsageRecorded(CurrentStep, used.InputTokens, used.OutputTokens, 0, cost, used.Pricing.Currency);
        await writer.Write(
            async services =>
            {
                var runs = services.GetRequiredService<RunRepository>();
                runs.Add(new UsageRecord
                {
                    RunId = RunId,
                    RootRunId = RootRunId,
                    BotId = BotId,
                    ModelConnectionId = used.ModelConnectionId,
                    Model = used.Model,
                    InputTokens = used.InputTokens,
                    OutputTokens = used.OutputTokens,
                    Cost = cost,
                    Currency = used.Pricing.Currency,
                });

                if (await runs.GetRunForUpdate(RunId, CancellationToken.None) is { } stored)
                {
                    stored.Usage = usage;
                }
            },
            [RunEventWriter.Event(RunId, RunEventTypes.UsageRecorded, recorded)]);
    }

    /// <summary>The tokens every model call of this run has read and written so far, earlier executions included.</summary>
    public long TokensUsed => usage.InputTokens + usage.OutputTokens;

    /// <summary>What this run may use before it is stopped: the space's limits, lowered by its bot's own.</summary>
    public RunCeilings Limits => limits;

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
            // A delegation whose child waits for a person is not over: it is asked again when the answer arrives.
            if (waitingOnChildren.ContainsKey(callId))
            {
                continue;
            }

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

        private int outputLeft = -1;

        private readonly Lock outputLock = new();

        #endregion

        #region Properties

        public bool Refused { get; set; }

        public long? StartedAt { get; set; }

        #endregion

        #region Public Methods

        /// <summary>True the first time only, so the event is written once however many callers race.</summary>
        public bool TryMarkRequested() => Interlocked.Exchange(ref requested, 1) == 0;

        public bool TryMarkCompleted() => Interlocked.Exchange(ref completed, 1) == 0;

        /// <summary>The part of <paramref name="text"/> the call may still stream, with a note in place of what is cut.</summary>
        public string TakeOutput(string text, int limit)
        {
            lock (outputLock)
            {
                if (outputLeft < 0)
                {
                    outputLeft = limit;
                }

                if (outputLeft == 0)
                {
                    return string.Empty;
                }

                if (text.Length < outputLeft)
                {
                    outputLeft -= text.Length;
                    return text;
                }

                var kept = text[..outputLeft];
                outputLeft = 0;
                return $"{kept}\n[The rest of the output is not shown here: a call shows at most {limit} characters.]\n";
            }
        }

        #endregion
    }

    #endregion
}
