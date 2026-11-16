namespace Drover.Engine.Runs;

/// <summary>
/// The engine's side of one run. The runtime asks it before every tool call and hands it every call to execute, so
/// the model never decides what it may do: the grant is read again when the call is made, the bot must still be
/// active, and the result is redacted before anything stores or sees it.
/// </summary>
public sealed class RunGovernor(
    RunSession session,
    Bot bot,
    ModelPricing pricing,
    IToolSource? toolServers,
    RunRules rules,
    ILogger logger,
    bool modelSeesImages = false,
    RunExecutor.ChildRuns? children = null) : IRunGovernor
{
    #region Interface Implementations

    public async Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        // The model call that asked for this tool is stored by now, so its message can be announced before the tool starts.
        await session.AnnounceStoredMessage();
        var decision = await DecideNow(toolCall, cancellationToken);

        if (decision.Verdict is ToolGateVerdict.Ask && session.IsRerun(toolCall.CallId) && await ResumesWithoutAsking(toolCall, cancellationToken))
        {
            decision = ToolGateDecision.Run with { Arguments = decision.Arguments };
        }

        if (decision.Verdict is ToolGateVerdict.Refuse)
        {
            session.MarkRefused(toolCall.CallId);
            logger.LogInformation("Tool {ToolName} refused in run {RunId}: {Reason}", toolCall.ToolName, session.RunId, decision.Reason);
        }

        // A call a person approved runs on ask: the gate holds the approval, so it is no reason to settle the call.
        var mayRun = decision.Verdict is ToolGateVerdict.Run || (decision.Verdict is ToolGateVerdict.Ask && toolCall.Approved);

        if (!mayRun && session.IsRerun(toolCall.CallId))
        {
            // A cut-off call that may no longer run is settled as interrupted by the runtime; say so in the log too.
            await session.CompleteTool(toolCall.CallId, Error("interrupted, outcome unknown"), "interrupted");
        }

        return decision;
    }

    public async Task<ToolOutcome> InvokeTool(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A tool was called inside a tenant transaction. Close the scope before the call.");
        }

        try
        {
            await session.StartTool(toolCall.CallId);
            var outcome = await RuleOnResult(toolCall, await Run(toolCall, cancellationToken), cancellationToken);
            await session.CompleteTool(toolCall.CallId, outcome);
            return outcome;
        }
        catch (LeaseLostException taken)
        {
            // The run belongs to another claim now. Leaving as a cancellation keeps the runtime from recording an
            // outcome for a call that the new claim has already settled.
            throw new OperationCanceledException($"Run {session.RunId} was taken over during a tool call.", taken);
        }
    }

    public async Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken)
    {
        var tool = session.FindToolByModelName(BuiltinTool.ModelName(DelegateTool.Name));

        if (children is null || tool is not { IsDelegation: true } || request is not { ParentCallId: { } callId, BotId: not null })
        {
            return new ChildRun(Guid.Empty, request.AgentName) { Refusal = Error("This bot may not hand work to other bots.") };
        }

        await session.StartTool(callId);
        var child = await children.Begin(request, tool.Tool.Id, cancellationToken);

        if (child.Refusal is not null)
        {
            session.MarkRefused(callId);
            logger.LogInformation("Delegation from run {RunId} refused: {Refusal}", session.RunId, child.Refusal.Result.GetRawText());
        }

        return child;
    }

    public Task<ChildRunReport> RunChildRun(ChildRun child, CancellationToken cancellationToken) =>
        children?.Execute(child, cancellationToken) ?? throw new InvalidOperationException("This run cannot run child runs.");

    /// <summary>
    /// The child's answer becomes the call's result: its last reply when it completed, what went wrong when it
    /// failed, "cancelled" when it was stopped while this run went on. A child that waits for a person pauses this
    /// run with it, and a child stopped because this run was stopped stops this run too. The result passes this bot's
    /// rules on tool results, as any tool's does, before it is stored or shown.
    /// </summary>
    public async Task<ToolOutcome> CompleteChildRun(ChildRun child, ChildRunReport report, CancellationToken cancellationToken)
    {
        if (children is null)
        {
            throw new InvalidOperationException("This run cannot run child runs.");
        }

        var callId = children.CallOf(child);

        // What a tainted child hands back carries the untrusted text it read. This run is tainted with it before
        // anything else, so its own consequential tools ask from its next model call on, whatever the child came to.
        if (report.Tainted)
        {
            await session.Taint();
        }

        await StopWithChild(child, report, callId);
        var tool = session.FindToolByModelName(BuiltinTool.ModelName(DelegateTool.Name));
        var request = new ToolCallRequest { RunId = session.RunId, CallId = callId, ToolName = DelegateTool.Name, ToolId = tool?.Tool.Id, Arguments = default };
        var outcome = await RuleOnResult(request, ToResult(child, report), cancellationToken);
        var toolCallId = ToolCallIds.For(session.RunId, callId);
        await session.Writer.Append(
            session.RunId,
            RunEventTypes.DelegationCompleted,
            new DelegationCompleted(toolCallId, child.RunId, child.BotId, report.Outcome.ToString().ToLowerInvariant(), session.Preview(outcome.Result)));
        await session.CompleteTool(callId, outcome);
        return outcome;
    }

    public Task RecordUsage(ModelUsage usage, CancellationToken cancellationToken) =>
        Record(new ToolModelUsage(bot.ModelConnectionId, bot.Model, pricing, usage.InputTokens, usage.OutputTokens));

    public JsonElement Redact(JsonElement value) => session.Redactor.Redact(value);

    public async Task<bool> HoldsReplies(CancellationToken cancellationToken) =>
        (await rules.Load(cancellationToken)).Any(rule => rule.Checkpoint is RuleCheckpoint.BeforeReply);

    public async Task<string> RuleOnReply(string text, CancellationToken cancellationToken)
    {
        if (await rules.Check(new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = text }, null, cancellationToken) is not { } outcome)
        {
            return text;
        }

        if (outcome.Effect is RuleEffect.Redact)
        {
            return outcome.RedactedText ?? text;
        }

        var withheld = $"[This reply was withheld by the rule \"{outcome.Rule.Name}\": {outcome.Why}.]";

        if (outcome.Effect is RuleEffect.Stop)
        {
            session.EndByRule(RunRules.StoppedByRule, $"Stopped by the rule \"{outcome.Rule.Name}\": {outcome.Why}.");
        }

        return withheld;
    }

    public async Task<JsonElement> RuleOnCallArguments(string callId, string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        // The same call read again (a stream and then its summary, or the call as the rules already left it)
        // keeps its decision. The same id with other arguments is another call and is decided afresh.
        if (session.RuledCalls.TryGetValue(callId, out var known)
            && (JsonElement.DeepEquals(known.Written, arguments) || JsonElement.DeepEquals(known.Left, arguments)))
        {
            return known.Left;
        }

        session.NoteCall(callId);

        // Decided here, on the arguments as the model wrote them, and written down by the gate when the call
        // reaches it. From here on the call exists only as the rules leave it. A call to a tool the bot does not
        // have never runs, and is still stored only as the rules leave it.
        var tool = session.FindToolByModelName(toolName);
        var subject = tool is null
            ? new RuleSubject { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = toolName, Json = arguments }
            : CallSubject(tool, arguments);
        var outcome = await rules.Find(subject, cancellationToken);
        var left = outcome?.RedactedJson ?? arguments;
        session.RuledCalls[callId] = new RuledCall(arguments.Clone(), left.Clone(), outcome);
        return left;
    }

    /// <summary>
    /// A call is waiting for a person. When a rule is why, the rule is written down before the question, so the
    /// person reads which rule asked.
    /// </summary>
    public async Task RuleOnApprovalRequest(string callId, string modelName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (session.FindToolByModelName(modelName) is not { } tool)
        {
            return;
        }

        if (await RuleFor(callId, tool, arguments, cancellationToken) is { Effect: RuleEffect.Ask } outcome)
        {
            await rules.Record(outcome, RuleCheckpoint.BeforeTool, ToolCallIds.For(session.RunId, callId));
        }
    }

    /// <summary>
    /// What the person asked about a call is shown beside its arguments, when the tool is a built-in one that says,
    /// redacted as the arguments are. Never throws for the tool: a call whose details cannot be read still waits, with
    /// none, and the reason is logged.
    /// </summary>
    public async Task<IReadOnlyList<ToolApprovalDetail>?> DescribeForApproval(
        string callId,
        string modelName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (session.FindToolByModelName(modelName)?.Builtin is not { DescribeForApproval: { } describe })
        {
            return null;
        }

        try
        {
            var call = new ToolCallRequest { RunId = session.RunId, CallId = callId, ToolName = modelName, Arguments = arguments };
            var details = await describe(BuiltinCall(call, []), cancellationToken);
            return [.. details.Select(detail => detail with { Text = session.Redactor.Redact(detail.Text) })];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogInformation(exception, "The details of tool {ToolName} waiting in run {RunId} could not be read", modelName, session.RunId);
            return null;
        }
    }

    public async Task RuleOnArrivals(IReadOnlyList<Message> messages, CancellationToken cancellationToken)
    {
        if (await rules.CheckArrivals(messages, cancellationToken) is { } refused)
        {
            throw new OperationCanceledException(refused);
        }
    }

    #endregion

    #region Gate

    private async Task<ToolGateDecision> DecideNow(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (toolCall.ToolId is not { } toolId || session.FindTool(toolId) is not { } tool)
        {
            return ToolGateDecision.Refuse($"The tool {toolCall.ToolName} is not one this bot was given.");
        }

        var (status, effect) = await session.Tenant.Transact(
            async services =>
            {
                var current = await services.GetRequiredService<BotRepository>().FindBot(bot.Id, cancellationToken);
                var grant = await services.GetRequiredService<ToolRepository>().FindEffectiveGrant(bot.Id, toolId, cancellationToken);
                return (current?.Status, grant);
            },
            cancellationToken);

        if (status is not BotStatus.Active)
        {
            return ToolGateDecision.Refuse("This bot is paused, so it may not use tools right now.");
        }

        // The grant as it stands now, and then the run's own state: a tainted run asks before it changes anything.
        var granting = (effect is { } granted ? session.EffectNow(tool, granted) : effect) switch
        {
            GrantEffect.Allow => ToolGateDecision.Run,
            GrantEffect.Ask => ToolGateDecision.Ask,
            _ => ToolGateDecision.Refuse($"This bot has no grant for the tool {toolCall.ToolName}."),
        };

        // Rules come after the grant and only narrow: what the grant refuses, no rule is asked about.
        return granting.Verdict is ToolGateVerdict.Refuse ? granting : await RuleOnCall(toolCall, tool, granting, cancellationToken);
    }

    private async Task<ToolGateDecision> RuleOnCall(ToolCallRequest toolCall, RunTool tool, ToolGateDecision granting, CancellationToken cancellationToken)
    {
        if (await RuleFor(toolCall.CallId, tool, toolCall.Arguments, cancellationToken) is not { } outcome)
        {
            return granting;
        }

        // A rule that asks was written down when the person was asked. Their yes is not a second firing.
        if (outcome.Effect is not RuleEffect.Ask || !toolCall.Approved)
        {
            await rules.Record(outcome, RuleCheckpoint.BeforeTool, ToolCallIds.For(session.RunId, toolCall.CallId));
        }

        switch (outcome.Effect)
        {
            case RuleEffect.Stop:
                session.EndByRule(RunRules.StoppedByRule, $"Stopped by the rule \"{outcome.Rule.Name}\": {outcome.Why}.");
                return ToolGateDecision.Refuse($"The rule \"{outcome.Rule.Name}\" stopped the run: {outcome.Why}.");
            case RuleEffect.Block:
                return ToolGateDecision.Refuse($"The rule \"{outcome.Rule.Name}\" does not allow this: {outcome.Why}.");
            case RuleEffect.Ask:
                // A person is asked about the call as the other rules leave it.
                return ToolGateDecision.Ask with { Arguments = outcome.RedactedJson };
            default:
                return granting with { Arguments = outcome.RedactedJson };
        }
    }

    // What the rules say about a call: what they said when the model wrote it, if this execution saw that, and
    // otherwise what they say about the arguments now (a call taken up again after a wait or a restart, whose
    // stored arguments already are what the rules left).
    private async Task<RuleOutcome?> RuleFor(string callId, RunTool tool, JsonElement arguments, CancellationToken cancellationToken) =>
        session.RuledCalls.TryGetValue(callId, out var ruled) && JsonElement.DeepEquals(ruled.Left, arguments)
            ? ruled.Outcome
            : await rules.Find(CallSubject(tool, arguments), cancellationToken);

    private static RuleSubject CallSubject(RunTool tool, JsonElement arguments) => new()
    {
        Checkpoint = RuleCheckpoint.BeforeTool,
        ToolName = tool.Tool.Name,
        ToolKind = tool.Tool.Sensitivity,
        Json = arguments,
    };

    private async Task<ToolOutcome> RuleOnResult(ToolCallRequest toolCall, ToolOutcome result, CancellationToken cancellationToken)
    {
        if (toolCall.ToolId is not { } toolId || session.FindTool(toolId) is not { } tool)
        {
            return result;
        }

        var subject = new RuleSubject
        {
            Checkpoint = RuleCheckpoint.AfterTool,
            ToolName = tool.Tool.Name,
            ToolKind = tool.Tool.Sensitivity,
            Json = result.Result,
        };

        if (await rules.Check(subject, ToolCallIds.For(session.RunId, toolCall.CallId), cancellationToken) is not { } outcome)
        {
            return result;
        }

        if (outcome.Effect is RuleEffect.Redact)
        {
            return result with { Result = outcome.RedactedJson ?? result.Result };
        }

        if (outcome.Effect is RuleEffect.Stop)
        {
            session.EndByRule(RunRules.StoppedByRule, $"Stopped by the rule \"{outcome.Rule.Name}\": {outcome.Why}.");
        }

        // The tool ran; what it answered is kept from the model.
        return Error($"The rule \"{outcome.Rule.Name}\" withheld this tool's result: {outcome.Why}.");
    }

    private async Task<ToolOutcome> Run(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (toolCall.ToolId is { } remoteId && session.FindTool(remoteId) is { Connection: not null } remote)
        {
            return await CallServer(remote, toolCall, cancellationToken);
        }

        if (toolCall.ToolId is not { } toolId || session.FindTool(toolId)?.Builtin is not { } builtin)
        {
            return Error("This server has nothing that runs that tool.");
        }

        try
        {
            List<ToolImage> images = [];
            var result = await builtin.Run(BuiltinCall(toolCall, images), cancellationToken);
            return new ToolOutcome(session.Redactor.Redact(result)) { Images = images };
        }
        catch (BuiltinToolException refused)
        {
            logger.LogInformation("Tool {ToolName} could not do what was asked in run {RunId}: {Reason}", toolCall.ToolName, session.RunId, refused.Message);
            return Error(refused.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed in run {RunId}", toolCall.ToolName, session.RunId);
            return Error("The tool failed.");
        }
    }

    /// <summary>
    /// The call as a built-in tool sees it: its arguments, its run, and a way to stream output, to taint the run, to
    /// attach a picture for the model's next call, and to call another model for the run within its limits.
    /// </summary>
    private BuiltinToolCall BuiltinCall(ToolCallRequest toolCall, List<ToolImage> images) =>
        new(
            toolCall.Arguments,
            bot.Id,
            session.RunId,
            ToolCallIds.For(session.RunId, toolCall.CallId),
            session.Tenant,
            chunk => session.ToolOutput(toolCall.CallId, chunk),
            session.Taint,
            images.Add)
        {
            ModelSeesImages = modelSeesImages,
            BeginModelCall = session.BeginToolModelCall,
            RecordModelUsage = Record,
        };

    #endregion

    #region Tool Servers

    /// <summary>
    /// Calls a tool on its server. The credential is read in a short transaction as the run's own person, so the
    /// database returns that person's credential or the space's and nobody else's; the transaction is closed before
    /// the call. What comes back is a third party's text: it is scrubbed of the credential, cut to size, and returned
    /// as a tool result and nothing else. If the connection is not trusted, the run is tainted from here on.
    /// </summary>
    private async Task<ToolOutcome> CallServer(RunTool tool, ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (toolServers is null)
        {
            return Error("This server has no adapter for tool connections.");
        }

        var (connection, resolved) = await session.Tenant.Transact(
            async services =>
            {
                var current = await services.GetRequiredService<ToolConnectionRepository>().FindConnection(tool.Connection!.Id, cancellationToken);
                return current is null or { Status: ConnectionStatus.Disabled }
                    ? (current, new ResolvedToolEndpoint(null, "This tool's connection was removed or switched off.", []))
                    : (current, await services.GetRequiredService<ToolConnectionService>().Resolve(current, session.Tenant.UserId, cancellationToken));
            },
            cancellationToken);

        if (resolved.Endpoint is not { } endpoint || connection is null)
        {
            return Error(resolved.Problem ?? "This tool's connection cannot be used.");
        }

        ToolOutcome outcome;

        try
        {
            outcome = await toolServers.CallTool(
                endpoint with { Timeout = session.Options.ToolCallTimeout },
                tool.Tool.Name,
                toolCall.Arguments,
                cancellationToken);
        }
        catch (ToolSourceException failure) when (failure.Failure is ToolSourceFailure.SignInRequired && endpoint.CredentialKey is { } credentialId)
        {
            // The tokens stopped working and could not be renewed. Nobody can be asked from inside a run, so the
            // credential is marked, the person sees "sign in again" on the connection, and the model is told why.
            logger.LogInformation("Tool {ToolName} on connection {ConnectionId} needs a new sign-in in run {RunId}", tool.Tool.Name, connection.Id, session.RunId);
            await session.Tenant.InScope(
                services => services.GetRequiredService<ToolConnectionService>().MarkNeedsReconnect(connection.Id, credentialId, cancellationToken));
            return Error(
                connection.CredentialScope is CredentialScope.Space
                    ? $"The {connection.Name} connection's sign-in has stopped working. An admin must sign in to it again."
                    : $"The {connection.Name} connection needs you to sign in again.");
        }
        catch (ToolSourceException failure)
        {
            logger.LogWarning("Tool {ToolName} on connection {ConnectionId} failed in run {RunId}: {Failure}", tool.Tool.Name, connection.Id, session.RunId, failure.Failure);
            return Error($"The {connection.Name} server could not be used. {failure.Message}");
        }

        if (connection.Trust is TrustLevel.Untrusted)
        {
            await session.Taint();
        }

        var scrubbed = new Redactor(resolved.Secrets).Redact(session.Redactor.Redact(outcome.Result));
        return new ToolOutcome(Cut(scrubbed), outcome.IsError);
    }

    /// <summary>A result too long for a model is cut to the limit, and says that it was.</summary>
    private JsonElement Cut(JsonElement result)
    {
        var text = result.ValueKind is JsonValueKind.String ? result.GetString() ?? string.Empty : result.GetRawText();
        var limit = session.Options.MaxToolResultCharacters;
        return text.Length <= limit
            ? result
            : JsonSerializer.SerializeToElement($"{text[..limit]}\n[The result was {text.Length} characters long. Only the first {limit} are shown.]");
    }

    #endregion

    #region Delegation

    /// <summary>
    /// A delegation cut off by a restart or a wait is asked again, and asking again must not undo what was decided: once
    /// its child run has begun it goes on as the same child, even when its grant is ask or this run has since been
    /// tainted. A grant that no longer allows the call still refuses it; only asking is not repeated. (A call a person
    /// approved never reaches this: its approval is handed over again and it comes back through the gate.)
    /// </summary>
    private async Task<bool> ResumesWithoutAsking(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        var row = await session.Tenant.Transact(
            services => services.GetRequiredService<ToolCallRepository>().GetToolCall(ToolCallIds.For(session.RunId, toolCall.CallId), cancellationToken),
            cancellationToken);
        return row is { ChildRunId: not null } && toolCall.ToolId is { } toolId && session.FindTool(toolId) is { IsDelegation: true };
    }

    /// <summary>Throws when this run must stop with its child: the child waits for a person, or this run was stopped.</summary>
    private async Task StopWithChild(ChildRun child, ChildRunReport report, string callId)
    {
        if (report.Outcome is ChildRunOutcome.Waiting)
        {
            logger.LogInformation("Run {RunId} waits with its child run {ChildRunId}", session.RunId, child.RunId);
            session.PauseForChild(callId);
            throw new OperationCanceledException($"Run {session.RunId} waits for its child run {child.RunId}.");
        }

        // A child cancelled together with this run (the whole tree was asked to stop) stops this run as well, even when
        // this run's own watch has not seen the request yet.
        if (report.Outcome is ChildRunOutcome.Cancelled && !session.Stopped.IsCancellationRequested && await children!.IsParentCancelRequested())
        {
            session.CancelByUser();
        }

        if (session.Stopped.IsCancellationRequested)
        {
            throw new OperationCanceledException($"Run {session.RunId} was stopped while its child run {child.RunId} worked.");
        }
    }

    private static ToolOutcome ToResult(ChildRun child, ChildRunReport report) => report.Outcome switch
    {
        ChildRunOutcome.Completed => new ToolOutcome(JsonSerializer.SerializeToElement(
            string.IsNullOrWhiteSpace(report.Answer) ? $"{child.AgentName} finished without an answer." : report.Answer)),
        ChildRunOutcome.Cancelled => new ToolOutcome(
            JsonSerializer.SerializeToElement(new { error = $"{child.AgentName} was cancelled before it finished the task.", outcome = "cancelled" }),
            true),
        _ => Error($"{child.AgentName} could not finish the task: {report.Answer ?? "no reason was given."}"),
    };

    #endregion

    #region Private Methods

    private static ToolOutcome Error(string message) => new(JsonSerializer.SerializeToElement(new { error = message }), true);

    /// <summary>
    /// Records one model call against the run: the run's own, or one a tool made for it, such as reading the screen.
    /// The record names the model that answered, so a call by another model is told apart from the bot's own.
    /// </summary>
    private async Task Record(ToolModelUsage usage)
    {
        var cost = Cost(usage);
        session.AddUsage(usage.InputTokens, usage.OutputTokens, cost);
        var recorded = new UsageRecorded(session.CurrentStep, usage.InputTokens, usage.OutputTokens, 0, cost, usage.Pricing.Currency);
        await session.Writer.Write(
            async services =>
            {
                var runs = services.GetRequiredService<RunRepository>();
                runs.Add(new UsageRecord
                {
                    RunId = session.RunId,
                    RootRunId = session.RootRunId,
                    BotId = bot.Id,
                    ModelConnectionId = usage.ModelConnectionId,
                    Model = usage.Model,
                    InputTokens = usage.InputTokens,
                    OutputTokens = usage.OutputTokens,
                    Cost = cost,
                    Currency = usage.Pricing.Currency,
                });

                if (await runs.GetRunForUpdate(session.RunId, CancellationToken.None) is { } run)
                {
                    run.Usage = session.Usage;
                }
            },
            [RunEventWriter.Event(session.RunId, RunEventTypes.UsageRecorded, recorded)]);
    }

    /// <summary>What a model call cost at the prices the catalog lists for its model.</summary>
    internal static decimal Cost(ToolModelUsage usage) =>
        ((usage.InputTokens * (usage.Pricing.InputPerMillion ?? 0)) + (usage.OutputTokens * (usage.Pricing.OutputPerMillion ?? 0))) / 1_000_000m;

    #endregion
}
