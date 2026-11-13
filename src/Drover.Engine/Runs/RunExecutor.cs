namespace Drover.Engine.Runs;

/// <summary>
/// Executes one claimed run to the point where it ends or waits. It reads what the run needs in one short
/// transaction, as the run's own person in the run's own space, so row-level security applies to a worker exactly as
/// it does to a request; then it runs the agent with no transaction open, writing events, usage, and status in short
/// transactions of their own. Nothing here is needed after a restart: a run taken over from a dead worker starts
/// from the rows the dead one left.
/// </summary>
public sealed class RunExecutor(
    RunScopes scopes,
    IAgentRuntime runtime,
    IEnumerable<IModelClientProvider> providers,
    IEnumerable<BuiltinTool> builtins,
    IEnumerable<IToolSource> toolSources,
    RunEventHub hub,
    RunSignal signal,
    UserSignalHub signals,
    IOptions<RunOptions> options,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILoggerFactory loggers)
{
    #region Private Fields

    private static readonly string[] stepEventTypes =
        [RunEventTypes.StepStarted, RunEventTypes.StepDiscarded, RunEventTypes.MessageCompleted];

    /// <summary>How long <c>sys.claim_runs</c> and a renewal lease a run for.</summary>
    private static readonly TimeSpan leaseDuration = TimeSpan.FromSeconds(45);

    private static readonly string[] toolEventTypes = [RunEventTypes.ToolRequested, RunEventTypes.ToolCompleted];

    private static readonly string[] approvalEventTypes = [RunEventTypes.ToolApprovalRequested, RunEventTypes.RunWaiting];

    private readonly ILogger logger = loggers.CreateLogger<RunExecutor>();

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs the claimed run as <paramref name="workerId"/>. <paramref name="stopping"/> is the server shutting down:
    /// the run is left as it is and its lease released, so the next server takes it over at once.
    /// </summary>
    public async Task Execute(ClaimedRun claimed, string workerId, CancellationToken stopping)
    {
        var tenant = scopes.For(claimed.SpaceId, claimed.PrincipalUserId);
        var loaded = await LoadOrGiveUp(tenant, claimed.RunId, workerId, stopping);

        if (loaded is null)
        {
            // Row-level security shows a run only inside its own space, so a worker handed an id from elsewhere finds nothing.
            logger.LogWarning("Run {RunId} is not this worker's to execute in space {SpaceId}", claimed.RunId, claimed.SpaceId);
            return;
        }

        await using var session = NewSession(loaded, tenant, workerId);

        // What the log already says about a call is not said again: a call an earlier execution saw through keeps
        // one tool.requested and one tool.completed, however the runtime reports it this time.
        foreach (var callId in loaded.RequestedCalls)
        {
            session.MarkRequested(callId);

            if (!loaded.UnsettledCalls.Contains(callId))
            {
                session.MarkSettled(callId);
            }
        }

        session.Writer.LeaseLost = session.LoseLease;
        using var executing = signal.Executing(claimed.RunId, session.CancelByUser);
        using var watchStop = new CancellationTokenSource();
        var watching = Watch(session, watchStop.Token);

        try
        {
            await Run(session, loaded, stopping);
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Run {RunId} was taken over by another worker; this one stops without writing", claimed.RunId);
        }
        finally
        {
            await watchStop.CancelAsync();
            await watching;
        }
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads the run, or deals with a run that cannot be loaded. A load that throws would otherwise leave the run
    /// leased, be claimed again when the lease lapses, and throw again, for ever, with its thread refusing every new
    /// message. So a failed load still honours a cancel request, and fails the run once it is out of attempts.
    /// </summary>
    private async Task<LoadedRun?> LoadOrGiveUp(RunTenant tenant, Guid runId, string workerId, CancellationToken stopping)
    {
        try
        {
            return await tenant.Transact(services => Load(services, runId, workerId, stopping), stopping);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(failure, "Run {RunId} could not be loaded", runId);
            await tenant.Transact(services => GiveUpIfDue(services, runId, workerId), CancellationToken.None);
            throw;
        }
    }

    private async Task GiveUpIfDue(IServiceProvider services, Guid runId, string workerId)
    {
        var run = await services.GetRequiredService<RunRepository>().GetRunForUpdate(runId, CancellationToken.None);

        if (run is not { Status: RunStatus.Running } || run.LeaseOwner != workerId)
        {
            return;
        }

        if (run.CancelRequestedAt is not null)
        {
            await End(services, run, RunStatus.Cancelled, RunReasons.CancelledByUser, null);
        }
        else if (run.Attempt + 1 >= options.Value.MaxAttempts)
        {
            await End(services, run, RunStatus.Failed, null, new RunError("internal", "The run could not be started. The server log has the details."));
        }
    }

    /// <summary>
    /// Ends a run from its bare row, for the cases where no session could be built. The event that reports it is
    /// written when the run's person can still see the thread; the status is set either way.
    /// </summary>
    private async Task End(IServiceProvider services, Run run, RunStatus status, string? reason, RunError? error)
    {
        run.Status = status;
        run.StatusReason = reason;
        run.Error = error;
        run.LeaseOwner = null;
        run.LeaseExpiresAt = null;
        run.FinishedAt = time.GetUtcNow();

        if (await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(run.ThreadId, CancellationToken.None) is { } thread)
        {
            var changed = new RunStatusChanged(status.ToWireName(), reason, error is null ? null : new RunProblem(error.Code, error.Message), run.Attempt, run.Usage.ToTotals());
            var stored = await services.GetRequiredService<RunEventRepository>()
                .Append(run.RootRunId, thread, [RunEventWriter.Event(run.Id, EventTypeFor(status), changed)], CancellationToken.None);
            hub.Publish(run.RootRunId, stored);

            if (await services.GetRequiredService<ThreadLine>().RunEnded(run, thread, CancellationToken.None) is { } next)
            {
                hub.Publish(next.Run.RootRunId, next.Events);
            }
        }

        signals.Publish(run.SpaceId, run.PrincipalUserId, new UserSignal(UserSignalKinds.RunFinished, run.ThreadId, run.Id));

        logger.LogWarning("Run {RunId} ended as {Status} before it could start", run.Id, status);
    }

    private async Task<LoadedRun?> Load(IServiceProvider services, Guid runId, string workerId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<RunRepository>().FindRun(runId, cancellationToken);

        if (run is not { Status: RunStatus.Running } || run.LeaseOwner != workerId)
        {
            return null;
        }

        var thread = await services.GetRequiredService<ThreadRepository>().FindThread(run.ThreadId, cancellationToken);
        var bot = await services.GetRequiredService<BotRepository>().FindBot(run.BotId, cancellationToken);
        var connections = services.GetRequiredService<ModelConnectionRepository>();
        var connection = bot is null ? null : await connections.FindConnection(bot.ModelConnectionId, cancellationToken);
        var entry = bot is null ? null : await connections.FindCatalogEntry(bot.ModelConnectionId, bot.Model, cancellationToken);
        var key = await RevealKey(services, connection, cancellationToken);
        var policy = await services.GetRequiredService<PolicyService>().GetEffective(run.SpaceId, cancellationToken);
        var granted = bot is null ? [] : await services.GetRequiredService<ToolRepository>().ListGrantedTools(bot.Id, cancellationToken);
        var toolConnectionRows = services.GetRequiredService<ToolConnectionRepository>();
        var toolConnections = await toolConnectionRows
            .ListConnections([.. granted.Select(tool => tool.Tool.ToolConnectionId).OfType<Guid>().Distinct()], cancellationToken);

        // A server's tool whose changed definition nobody has reviewed is left out of the run altogether: its new
        // name, description, and schema are a stranger's text, and offering the tool would put them in front of the model.
        var unreviewed = await toolConnectionRows.ListToolsNeedingReview(
            [.. granted.Where(tool => tool.Tool.SourceKind is ToolSourceKind.Mcp).Select(tool => tool.Tool.Id)],
            cancellationToken);
        granted = [.. granted.Where(tool => !unreviewed.Contains(tool.Tool.Id))];
        var events = services.GetRequiredService<RunEventRepository>();
        var steps = ReadSteps(await events.ListEventsOfRun(runId, stepEventTypes, cancellationToken));
        var openMessage = steps.Open is { } open
            ? await services.GetRequiredService<MessageRepository>().FindMessage(open.MessageId, cancellationToken)
            : null;
        var toolEvents = await events.ListEventsOfRun(runId, toolEventTypes, cancellationToken);
        var unsettled = ReadUnsettledCalls(toolEvents);
        List<Guid> requested = [.. toolEvents.Where(stored => stored.Type == RunEventTypes.ToolRequested).Select(stored => RunEventTypes.Read<ToolRequested>(stored.Data.GetRawText()).ToolCallId)];
        var toolCalls = services.GetRequiredService<ToolCallRepository>();
        var rows = await toolCalls.ListToolCalls(unsettled, cancellationToken);
        var asked = ReadLastApprovalRequests(await events.ListEventsOfRun(runId, approvalEventTypes, cancellationToken));
        var answered = asked.Count == 0 ? [] : await services.GetRequiredService<MessageRepository>().ListAnsweredCalls(run.ThreadId, cancellationToken);
        var answers = ToAnswers(
            [.. asked.Where(request => !answered.Contains(request.ToolCallId))],
            await toolCalls.ListToolCalls([.. asked.Select(request => request.ToolCallId)], cancellationToken));
        var delivered = await services.GetRequiredService<MessageRepository>().ListDeliveredMessages(run.ThreadId, runId, cancellationToken);

        return new LoadedRun(run, thread, bot, connection, key.Value, key.Readable, policy, entry?.Pricing ?? new ModelPricing(), granted, steps, openMessage, unsettled, rows, answers, [.. delivered.Select(message => message.Id)], requested, toolConnections);
    }

    /// <summary>The connection's key, or the fact that it cannot be read: a lost key ring fails the run, it does not loop.</summary>
    private async Task<(string? Value, bool Readable)> RevealKey(IServiceProvider services, ModelConnection? connection, CancellationToken cancellationToken)
    {
        if (connection?.SecretId is not { } secretId)
        {
            return (null, true);
        }

        try
        {
            return (await services.GetRequiredService<ISecretVault>().Reveal(secretId, cancellationToken), true);
        }
        catch (Exception unreadable) when (unreadable is CryptographicException or KeyNotFoundException)
        {
            logger.LogError(unreadable, "The key of model connection {ConnectionId} could not be read", connection.Id);
            return (null, false);
        }
    }

    private static StepState ReadSteps(IReadOnlyList<RunEvent> events)
    {
        var count = 0;
        StepChanged? open = null;

        foreach (var stored in events)
        {
            switch (stored.Type)
            {
                case RunEventTypes.StepStarted:
                    open = RunEventTypes.Read<StepChanged>(stored.Data.GetRawText());
                    count = Math.Max(count, open.Step);
                    break;
                case RunEventTypes.StepDiscarded when open is not null
                    && RunEventTypes.Read<StepChanged>(stored.Data.GetRawText()).Step == open.Step:
                    open = null;
                    break;
                case RunEventTypes.MessageCompleted when open is not null
                    && RunEventTypes.Read<MessageCompleted>(stored.Data.GetRawText()).Message.Id == open.MessageId:
                    open = null;
                    break;
                default:
                    break;
            }
        }

        return new StepState(count, open);
    }

    /// <summary>The calls the log says were requested and never says finished.</summary>
    private static List<Guid> ReadUnsettledCalls(IReadOnlyList<RunEvent> events)
    {
        List<Guid> requested = [];

        foreach (var stored in events)
        {
            if (stored.Type == RunEventTypes.ToolRequested)
            {
                requested.Add(RunEventTypes.Read<ToolRequested>(stored.Data.GetRawText()).ToolCallId);
            }
            else
            {
                requested.Remove(RunEventTypes.Read<ToolCompleted>(stored.Data.GetRawText()).ToolCallId);
            }
        }

        return requested;
    }

    /// <summary>
    /// The approval requests the run last went to wait on. A run that waits saves its session with those requests in
    /// it, and the session is saved again only when the run next stops, so until then an execution hands the answers
    /// over: the first one after the answers arrive, and one that takes over from it after a crash. An answer is
    /// handed over only while its call has no result in the conversation. Once it has, the call is done, and
    /// answering it again would put a second result after later messages.
    /// </summary>
    private static List<ToolApprovalRequested> ReadLastApprovalRequests(IReadOnlyList<RunEvent> events)
    {
        List<ToolApprovalRequested> beforeLastWait = [];
        List<ToolApprovalRequested> since = [];

        foreach (var stored in events)
        {
            if (stored.Type == RunEventTypes.ToolApprovalRequested)
            {
                since.Add(RunEventTypes.Read<ToolApprovalRequested>(stored.Data.GetRawText()));
            }
            else
            {
                beforeLastWait = since;
                since = [];
            }
        }

        return beforeLastWait;
    }

    /// <summary>A request is answered "no" when its row says denied, and "yes" when the row is anything a yes leads to.</summary>
    private static List<PendingResponse> ToAnswers(IReadOnlyList<ToolApprovalRequested> asked, IReadOnlyList<ToolCall> rows) =>
    [
        .. asked
            .Select(request => (Request: request, Row: rows.FirstOrDefault(row => row.Id == request.ToolCallId)))
            .Where(pair => pair.Row is { Status: not ToolCallStatus.PendingApproval })
            .Select(pair => new ToolApproval(pair.Request.RequestId, pair.Request.CallId, pair.Row!.Status is not ToolCallStatus.Denied)),
    ];

    private RunSession NewSession(LoadedRun loaded, RunTenant tenant, string workerId)
    {
        // A run whose thread its person can no longer see fails before it writes an event, so the writer is never used then.
        var thread = loaded.Thread ?? new ChatThread { Id = loaded.Run.ThreadId, SpaceId = loaded.Run.SpaceId };
        var writer = new RunEventWriter(
            tenant,
            loaded.Run.RootRunId,
            thread,
            hub,
            options.Value,
            time,
            loggers.CreateLogger<RunEventWriter>(),
            new RunFence(workerId, loaded.Run.Attempt));
        // Two tools never share the name the model sees: the first keeps it, and a later one is left out.
        List<RunTool> tools =
        [
            .. loaded.Granted
                .Where(granted => granted.Effect is not GrantEffect.Deny && granted.Tool.State is AvailabilityState.Available)
                .Select(granted => ToRunTool(granted, loaded.ToolConnections))
                .OfType<RunTool>()
                .DistinctBy(tool => tool.ModelName, StringComparer.Ordinal),
        ];
        return new RunSession(
            loaded.Run,
            thread,
            workerId,
            tenant,
            writer,
            tools,
            new Redactor([loaded.Key]),
            options.Value,
            time,
            loaded.Steps.Count,
            loaded.Delivered,
            loaded.Policy.TaintedRunEscalation);
    }

    /// <summary>
    /// A granted tool as this run uses it. A built-in tool needs its code on this server. A server's tool needs its
    /// connection, switched on, and is named for the model with the connection's own prefix, so no server can take
    /// another's names or a built-in's.
    /// </summary>
    private RunTool? ToRunTool(GrantedTool granted, IReadOnlyList<ToolConnection> toolConnections)
    {
        if (granted.Tool.SourceKind is ToolSourceKind.Builtin)
        {
            return new RunTool(
                granted.Tool,
                granted.Effect,
                BuiltinTool.ModelName(granted.Tool.Name),
                builtins.FirstOrDefault(builtin => builtin.Name == granted.Tool.Name));
        }

        return toolConnections.FirstOrDefault(found => found.Id == granted.Tool.ToolConnectionId) is { Status: not ConnectionStatus.Disabled } connection
            ? new RunTool(granted.Tool, granted.Effect, ToolNames.ForModel(connection.Slug, granted.Tool.Name), null, connection)
            : null;
    }

    #endregion

    #region Running

    private async Task Run(RunSession session, LoadedRun loaded, CancellationToken stopping)
    {
        if (loaded.Thread is null)
        {
            await session.Tenant.Transact(
                services => SetStatus(services, session, RunStatus.Failed, null, new RunError("thread_not_visible", "The run's thread is no longer visible to the person it acts for.")),
                CancellationToken.None);
            logger.LogWarning("Run {RunId} failed: its thread is not visible to its person", session.RunId);
            return;
        }

        var placed = await session.Tenant.Transact(
            services => services.GetRequiredService<ThreadLine>().PlaceMessagesOf(loaded.Run, loaded.Thread, CancellationToken.None),
            CancellationToken.None);
        await session.Writer.Write(null, [.. RecoveryEvents(session, loaded), .. session.Delivered(placed)]);

        if (FindReasonNotToStart(loaded) is { } refusal)
        {
            await Finish(session, refusal.Status, refusal.Reason, refusal.Error);
            return;
        }

        var bot = loaded.Bot!;
        var connection = loaded.Connection!;
        var provider = providers.First(candidate => candidate.Kind == connection.Kind);
        var endpoint = new ModelEndpoint(connection.Kind, provider.FixedAddress ?? new Uri(connection.BaseUrl!))
        {
            Limits = new DiscoveryLimits(deployment.Value.Rules.MaxModelListBytes, deployment.Value.Rules.MaxModelsPerConnection),
        };
        var rules = new RunRules(session, loggers.CreateLogger<RunRules>());
        var governor = new RunGovernor(
            session,
            bot,
            loaded.Pricing,
            toolSources.FirstOrDefault(source => source.Kind is ToolSourceKind.Mcp),
            rules,
            loggers.CreateLogger<RunGovernor>());
        using var model = new MeteredModelClient(
            provider.CreateChatClient(endpoint, loaded.Key, bot.Model, bot.ModelSettings),
            session,
            connection.Id,
            loggers.CreateLogger<MeteredModelClient>());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping, session.Stopped);

        // Every outbound request the model adapter makes from here carries the space's rule on private addresses.
        using var outbound = OutboundAttempt.Begin(loaded.Policy);

        try
        {
            // The messages that started this run are checked before the model is given anything.
            if (await rules.CheckArrivals(await Arrived(session, loaded, placed), cancellation.Token) is not null)
            {
                await FinishAfterFailure(session, new OperationCanceledException(), stopping);
                return;
            }

            var outcome = await DriveToAStop(session, loaded, model, governor, rules, cancellation.Token);

            // A rule can end the run while its last model call still returns normally.
            if (session.EndedByRule is not null)
            {
                await FinishAfterFailure(session, new OperationCanceledException(), stopping);
                return;
            }

            await (outcome is RunOutcome.Waiting
                ? Finish(session, RunStatus.Waiting, RunReasons.AwaitingApproval, null)
                : Finish(session, RunStatus.Completed, null, null));
        }
        catch (Exception exception) when (exception is not LeaseLostException)
        {
            logger.LogDebug(exception, "Run {RunId} stopped before the agent finished", session.RunId);
            await FinishAfterFailure(session, exception, stopping);
        }
    }

    /// <summary>
    /// Runs the agent until it ends or waits. When the run becomes tainted part way, the agent is stopped before its
    /// next model call and built again, this time with its consequential tools asking, and goes on from the stored
    /// conversation. The answers a waiting run came back with are handed over once, to the first agent.
    /// </summary>
    private async Task<RunOutcome> DriveToAStop(
        RunSession session,
        LoadedRun loaded,
        IModelChatClient model,
        RunGovernor governor,
        RunRules rules,
        CancellationToken cancellationToken)
    {
        var answers = loaded.Answers;

        while (true)
        {
            session.MarkBuilt();
            session.RulesAtBuild = await rules.Load(cancellationToken);

            try
            {
                return await Drive(session, BuildExecution(session, loaded, model) with { Responses = answers }, governor, cancellationToken);
            }
            catch (Exception rebuild) when (Find<AgentRebuildRequiredException>(rebuild) is not null && !cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Run {RunId} read from an untrusted source; its agent is built again so its tools ask", session.RunId);
                await session.Writer.Write(null, session.CloseOpenWork());
                answers = [];
            }
        }
    }

    private async Task<RunOutcome> Drive(RunSession session, RunExecution execution, RunGovernor governor, CancellationToken cancellationToken)
    {
        var outcome = RunOutcome.Completed;

        await foreach (var happened in runtime.Execute(execution, governor, cancellationToken))
        {
            switch (happened)
            {
                case TextDelta delta:
                    await session.Delta("text", delta.Text);
                    break;
                case ReasoningDelta delta:
                    await session.Delta("reasoning", delta.Text);
                    break;
                case ToolCallRequested requested:
                    await session.RequestTool(requested.CallId, requested.ToolName, requested.Arguments);
                    break;
                case ToolCallFinished finished:
                    await session.CompleteTool(finished.CallId, new ToolOutcome(finished.Result, finished.IsError));
                    break;
                case ApprovalRequested approval:
                    await session.RequestTool(approval.CallId, approval.ToolName, approval.Arguments);
                    await governor.RuleOnApprovalRequest(approval.CallId, approval.ToolName, approval.Arguments, cancellationToken);
                    await session.Writer.Append(
                        session.RunId,
                        RunEventTypes.ToolApprovalRequested,
                        new ToolApprovalRequested(ToolCallIds.For(session.RunId, approval.CallId), approval.RequestId, approval.CallId));
                    break;
                case RunFinished ended:
                    outcome = ended.Outcome;
                    break;
                default:
                    break;
            }
        }

        return outcome;
    }

    /// <summary>
    /// What arrived for this execution. Until the run's first step that is every message of its person it holds: the
    /// one that started it, those placed on the thread just now, and those an execution that was cut off placed
    /// before it could check them. After the first step only what was placed just now is new.
    /// </summary>
    private static async Task<IReadOnlyList<Message>> Arrived(RunSession session, LoadedRun loaded, IReadOnlyList<Message> placed)
    {
        if (loaded.Steps.Count > 0)
        {
            return placed;
        }

        var held = await session.Tenant.Transact(
            async services =>
            {
                var messages = services.GetRequiredService<MessageRepository>();
                List<Message> found = [.. await messages.ListDeliveredMessages(loaded.Run.ThreadId, loaded.Run.Id, CancellationToken.None)];

                if (loaded.Run.TriggerMessageId is { } triggerId
                    && found.All(message => message.Id != triggerId)
                    && await messages.FindMessage(triggerId, CancellationToken.None) is { } trigger)
                {
                    found.Insert(0, trigger);
                }

                return found;
            },
            CancellationToken.None);
        return [.. held, .. placed.Where(message => held.All(known => known.Id != message.Id))];
    }

    private static RunExecution BuildExecution(RunSession session, LoadedRun loaded, IModelChatClient model) => new()
    {
        RunId = loaded.Run.Id,
        SpaceId = loaded.Run.SpaceId,
        ThreadId = loaded.Run.ThreadId,
        PrincipalUserId = loaded.Run.PrincipalUserId,
        Bot = new BotDefinition
        {
            BotId = loaded.Bot!.Id,
            Name = loaded.Bot.Name,
            Instructions = loaded.Bot.Instructions,
            ModelConnectionId = loaded.Bot.ModelConnectionId,
            Model = loaded.Bot.Model,
        },
        Tools = [.. loaded.Granted.Select(granted => session.FindTool(granted.Tool.Id)).OfType<RunTool>().Select(tool => ToDefinition(session, tool))],
        Model = model,
    };

    private static ToolDefinition ToDefinition(RunSession session, RunTool tool) => new()
    {
        ToolId = tool.Tool.Id,
        Name = tool.ModelName,
        Description = tool.Tool.Description,
        InputSchema = tool.Tool.InputSchema,
        Sensitivity = tool.Tool.Sensitivity,
        RequiresApproval = session.EffectNow(tool, tool.Effect) is GrantEffect.Ask || session.RuleAsks(tool),
        DefinedByServer = tool.Builtin is null,
    };

    #endregion

    #region Recovery

    /// <summary>
    /// What the log needs before the run goes on: that it started (again), and the end of anything an earlier worker
    /// left open. A step whose model call was cut off is discarded, or completed if its message did reach the
    /// thread. A tool call the log never saw finish is closed with what its row says, or as interrupted; a read-only
    /// one that was running is left open, because the runtime runs it again.
    /// </summary>
    private IEnumerable<NewRunEvent> RecoveryEvents(RunSession session, LoadedRun loaded)
    {
        var run = loaded.Run;
        yield return RunEventWriter.Event(
            run.Id,
            RunEventTypes.RunStarted,
            new RunStatusChanged(RunStatus.Running.ToWireName(), null, null, run.Attempt, null));

        if (loaded.Steps.Open is { } open)
        {
            yield return loaded.OpenStepMessage is { } stored
                ? RunEventWriter.Event(run.Id, RunEventTypes.MessageCompleted, new MessageCompleted(stored.ToResponse()))
                : RunEventWriter.Event(run.Id, RunEventTypes.StepDiscarded, open);
        }

        foreach (var callId in loaded.UnsettledCalls)
        {
            var row = loaded.UnsettledRows.FirstOrDefault(call => call.Id == callId);

            if (row?.Status is ToolCallStatus.PendingApproval or ToolCallStatus.Approved)
            {
                continue;
            }

            if (row is { Status: ToolCallStatus.Running } && session.FindTool(row.ToolId) is { IsKnownToOnlyRead: true })
            {
                logger.LogInformation("Read-only tool call {ToolCallId} of run {RunId} was cut off and will run again", callId, run.Id);
                session.ExpectRerun(callId);
                continue;
            }

            // The runtime may report this call's result once more as it goes on; the log has its end from here.
            session.MarkSettled(callId);
            yield return RunEventWriter.Event(run.Id, RunEventTypes.ToolCompleted, Settled(session, callId, row));
        }
    }

    private static ToolCompleted Settled(RunSession session, Guid callId, ToolCall? row) =>
        row is { Result: { } result, Status: not ToolCallStatus.Running }
            ? new ToolCompleted(callId, row.Status.ToString().ToLowerInvariant(), row.IsError, session.Preview(result), Duration(row))
            : new ToolCompleted(callId, "interrupted", true, "interrupted, outcome unknown", 0);

    private static long Duration(ToolCall row) =>
        row is { StartedAt: { } started, FinishedAt: { } finished } ? (long)(finished - started).TotalMilliseconds : 0;

    private Refusal? FindReasonNotToStart(LoadedRun loaded)
    {
        if (loaded.Run.CancelRequestedAt is not null)
        {
            return new Refusal(RunStatus.Cancelled, RunReasons.CancelledByUser, null);
        }

        if (loaded.Run.Attempt >= options.Value.MaxAttempts)
        {
            return new Refusal(
                RunStatus.Failed,
                RunReasons.LeaseLost,
                new RunError(RunReasons.LeaseLost, $"The run was interrupted {loaded.Run.Attempt} times and was not started again."));
        }

        if (loaded.Bot is not { Status: BotStatus.Active })
        {
            return Failed("bot_unavailable", "This bot is paused or removed, so it cannot answer.");
        }

        if (loaded.Connection is not { } connection || providers.FirstOrDefault(candidate => candidate.Kind == connection.Kind) is not { } provider
            || (provider.FixedAddress is null && !Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out _)))
        {
            return Failed("model_unavailable", "The bot's model connection is missing or this server has no adapter for it.");
        }

        if (!loaded.KeyReadable)
        {
            return Failed("model_unavailable", "The key of the bot's model connection could not be read. Save the key on the connection again.");
        }

        return null;
    }

    private static Refusal Failed(string code, string message) => new(RunStatus.Failed, null, new RunError(code, message));

    #endregion

    #region Finishing

    private async Task FinishAfterFailure(RunSession session, Exception failure, CancellationToken stopping)
    {
        if (session.LeaseLost || Find<LeaseLostException>(failure) is not null)
        {
            throw new LeaseLostException(session.RunId);
        }

        if (session.CancelledByUser)
        {
            await Finish(session, RunStatus.Cancelled, RunReasons.CancelledByUser, null);
            return;
        }

        if (session.EndedByRule is { } rule)
        {
            await Finish(session, RunStatus.Failed, rule.Reason, new RunError(rule.Reason, rule.Message));
            return;
        }

        if (stopping.IsCancellationRequested && failure is OperationCanceledException)
        {
            await ReleaseLease(session);
            return;
        }

        if (Find<RunLimitException>(failure) is { } limit)
        {
            await Finish(session, RunStatus.Failed, limit.Reason, new RunError(limit.Reason, limit.Message));
            return;
        }

        if (Find<ModelProviderException>(failure) is { } provider)
        {
            logger.LogWarning("Run {RunId} failed at the model provider: {Failure}", session.RunId, provider.Failure);
            await Finish(session, RunStatus.Failed, null, new RunError(provider.Failure.ToWireName(), provider.Message));
            return;
        }

        logger.LogError(failure, "Run {RunId} failed", session.RunId);
        await Finish(session, RunStatus.Failed, null, new RunError("internal", "The run failed unexpectedly. The server log has the details."));
    }

    /// <summary>
    /// Sets the run's status and appends the event that reports it, in one transaction, after closing whatever the
    /// run left open. If another worker holds the run by now, nothing is written.
    /// </summary>
    private async Task Finish(RunSession session, RunStatus status, string? reason, RunError? error)
    {
        var closing = session.CloseOpenWork();

        if (closing.Count == 0)
        {
            await session.AnnounceStoredMessage();
        }

        var changed = new RunStatusChanged(status.ToWireName(), reason, error is null ? null : new RunProblem(error.Code, error.Message), session.Attempt, session.Usage.ToTotals());
        (Run Run, ChatThread Thread)? ended = null;
        QueuedRun? next = null;
        await session.Writer.Write(
            async services => ended = await SetStatus(services, session, status, reason, error),
            [.. closing, RunEventWriter.Event(session.RunId, EventTypeFor(status), changed)],
            async services => next = ended is { } over && status is not RunStatus.Waiting
                ? await services.GetRequiredService<ThreadLine>().RunEnded(over.Run, over.Thread, CancellationToken.None)
                : null);
        logger.LogInformation("Run {RunId} is {Status} ({Reason})", session.RunId, status, reason);

        if (next is not null)
        {
            hub.Publish(next.Run.RootRunId, next.Events);
            signals.Publish(next.Run.SpaceId, next.Run.PrincipalUserId, new UserSignal(UserSignalKinds.ThreadChanged, next.Run.ThreadId, next.Run.Id));
        }

        // A slot is free and the thread can take its next message.
        signal.Wake();
        signals.Publish(
            session.Tenant.SpaceId,
            session.Tenant.UserId,
            new UserSignal(
                status is RunStatus.Waiting ? UserSignalKinds.ApprovalWaiting : UserSignalKinds.RunFinished,
                session.Thread.Id,
                session.RunId));
    }

    /// <summary>
    /// The fenced status change. Returns the run and its thread, both locked, or null when the run's person cannot
    /// see the thread any more. The run row is locked first and the thread second, as everywhere a run is ended.
    /// </summary>
    private async Task<(Run Run, ChatThread Thread)?> SetStatus(IServiceProvider services, RunSession session, RunStatus status, string? reason, RunError? error)
    {
        var run = await services.GetRequiredService<RunRepository>().GetRunForUpdate(session.RunId, CancellationToken.None);

        if (run is not { Status: RunStatus.Running } || run.LeaseOwner != session.Fence.Worker || run.Attempt != session.Fence.Attempt)
        {
            throw new LeaseLostException(session.RunId);
        }

        var now = time.GetUtcNow();
        run.Status = status;
        run.StatusReason = reason;
        run.Error = error;
        run.LeaseOwner = null;
        run.LeaseExpiresAt = null;
        run.Usage = session.Usage;
        run.SubtreeUsage = session.Usage;

        if (status is not RunStatus.Waiting)
        {
            run.FinishedAt = now;
        }

        if (await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(run.ThreadId, CancellationToken.None) is not { } thread)
        {
            return null;
        }

        thread.LastActivityAt = now;
        return (run, thread);
    }

    private Task ReleaseLease(RunSession session)
    {
        logger.LogInformation("The server is stopping; run {RunId} is released for another worker", session.RunId);
        return session.Tenant.Transact(
            services => services.GetRequiredService<RunRepository>().ReleaseLease(session.RunId, session.Fence, CancellationToken.None),
            CancellationToken.None);
    }

    private static string EventTypeFor(RunStatus status) => status switch
    {
        RunStatus.Waiting => RunEventTypes.RunWaiting,
        RunStatus.Completed => RunEventTypes.RunCompleted,
        RunStatus.Cancelled => RunEventTypes.RunCancelled,
        _ => RunEventTypes.RunFailed,
    };

    private static T? Find<T>(Exception? failure)
        where T : Exception =>
        failure switch
        {
            null => null,
            T found => found,
            AggregateException many => many.InnerExceptions.Select(Find<T>).FirstOrDefault(found => found is not null),
            _ => Find<T>(failure.InnerException),
        };

    #endregion

    #region Watching

    /// <summary>
    /// Looks at the run row while the run executes: renews the lease, and stops the run when a person asked for that
    /// or when another worker has taken it. This is how a cancel reaches a run on another server.
    /// </summary>
    private async Task Watch(RunSession session, CancellationToken stop)
    {
        var renewedAt = time.GetTimestamp();
        var lastRead = time.GetTimestamp();

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.WatchInterval, time, stop);
                var renew = time.GetElapsedTime(renewedAt) >= options.Value.LeaseRenewInterval;
                var lease = await ReadLease(session, renew, stop);
                renewedAt = renew ? time.GetTimestamp() : renewedAt;
                lastRead = time.GetTimestamp();

                if (!lease.Held)
                {
                    session.LoseLease();
                    return;
                }

                if (lease.CancelRequested)
                {
                    session.CancelByUser();
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogTrace("Run {RunId} is no longer watched", session.RunId);
                return;
            }
            catch (ObjectDisposedException)
            {
                // The host is gone. Nothing of this run may touch the database again.
                logger.LogTrace("Run {RunId} outlived its host", session.RunId);
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not read the lease of run {RunId}; trying again", session.RunId);

                // A lease nobody could renew for this long has lapsed, and another claim may hold the run. Stop.
                if (time.GetElapsedTime(lastRead) >= leaseDuration)
                {
                    session.LoseLease();
                    return;
                }
            }
        }
    }

    private static Task<LeaseState> ReadLease(RunSession session, bool renew, CancellationToken cancellationToken) =>
        session.Tenant.Transact(
            async services =>
            {
                var runs = services.GetRequiredService<RunRepository>();

                if (renew && !await runs.RenewLease(session.RunId, session.Fence, cancellationToken))
                {
                    return new LeaseState(false, false);
                }

                return await runs.ReadLease(session.RunId, session.Fence, cancellationToken);
            },
            cancellationToken);

    #endregion

    #region Nested Types

    private sealed record StepState(int Count, StepChanged? Open);

    private sealed record Refusal(RunStatus Status, string? Reason, RunError? Error);

    private sealed record LoadedRun(
        Run Run,
        ChatThread? Thread,
        Bot? Bot,
        ModelConnection? Connection,
        string? Key,
        bool KeyReadable,
        EffectivePolicy Policy,
        ModelPricing Pricing,
        IReadOnlyList<GrantedTool> Granted,
        StepState Steps,
        Message? OpenStepMessage,
        IReadOnlyList<Guid> UnsettledCalls,
        IReadOnlyList<ToolCall> UnsettledRows,
        IReadOnlyList<PendingResponse> Answers,
        IReadOnlyList<Guid> Delivered,
        IReadOnlyList<Guid> RequestedCalls,
        IReadOnlyList<ToolConnection> ToolConnections);

    #endregion
}
