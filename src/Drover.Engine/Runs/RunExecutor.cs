namespace Drover.Engine.Runs;

/// <summary>
/// Executes one claimed run to the point where it ends or waits. It reads what the run needs in one short
/// transaction, as the run's own person in the run's own space, so row-level security applies to a worker exactly as
/// it does to a request; then it runs the agent with no transaction open, writing events, usage, and status in short
/// transactions of their own. Nothing here is needed after a restart: a run taken over from a dead worker starts
/// from the rows the dead one left.
/// </summary>
public sealed partial class RunExecutor(
    RunScopes scopes,
    IAgentRuntime runtime,
    IEnumerable<IModelClientProvider> providers,
    IEnumerable<BuiltinTool> builtins,
    IEnumerable<IToolSource> toolSources,
    RunEventHub hub,
    RunSignal signal,
    UserSignalHub signals,
    PushNotifier push,
    ConversationSummarizer summarizer,
    IFileStore fileStore,
    IEnumerable<SandboxBroker> sandboxBrokers,
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
    /// the run is left as it is and its lease released, so the next server takes it over at once. A run on a main
    /// thread that completed is followed by the thread's summary, once the run has ended and its person has been told.
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

        if (await ExecuteLoaded(loaded, tenant, workerId, stopping) && loaded.Thread is { IsMain: true })
        {
            await summarizer.Summarize(tenant, loaded.Run, stopping);
        }
    }

    #endregion

    #region Executing

    /// <summary>Executes a loaded run until it ends or waits. True when it completed.</summary>
    private async Task<bool> ExecuteLoaded(LoadedRun loaded, RunTenant tenant, string workerId, CancellationToken stopping)
    {
        await using var session = NewSession(loaded, tenant, workerId);

        try
        {
            return await ExecuteSession(session, loaded, stopping, stopping) is RunStatus.Completed;
        }
        catch (LeaseLostException)
        {
            logger.LogWarning("Run {RunId} was taken over by another worker; this one stops without writing", loaded.Run.Id);
            return false;
        }
    }

    /// <summary>
    /// Executes a run whose session is open, watching its row while it works. Returns the status it ended or waits
    /// with, or null when it was left as it is for another execution to take up.
    /// </summary>
    private async Task<RunStatus?> ExecuteSession(RunSession session, LoadedRun loaded, CancellationToken stopping, CancellationToken serverStopping)
    {
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
        using var executing = signal.Executing(loaded.Run.Id, session.CancelByUser);
        using var watchStop = new CancellationTokenSource();
        var watching = Watch(session, watchStop.Token);

        try
        {
            return await Run(session, loaded, stopping, serverStopping);
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
        await services.GetRequiredService<RunRepository>().AddToSubtreeUsage(run.Id, run.Usage, CancellationToken.None);

        if (await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(run.ThreadId, CancellationToken.None) is { } thread)
        {
            var changed = new RunStatusChanged(status.ToWireName(), reason, error is null ? null : new RunProblem(error.Code, error.Message), run.Attempt, run.Usage.ToTotals());
            var stored = await services.GetRequiredService<RunEventRepository>()
                .Append(run.RootRunId, thread, [RunEventWriter.Event(run.Id, EventTypeFor(status), changed)], CancellationToken.None);
            hub.Publish(run.RootRunId, stored);
            hub.Publish(run.RootRunId, await services.GetRequiredService<ThreadLine>().EndOpenDescendants(run, CancellationToken.None));

            if (await services.GetRequiredService<ThreadLine>().RunEnded(run, thread, CancellationToken.None) is { } next)
            {
                hub.Publish(next.Run.RootRunId, next.Events);
            }
        }

        signals.Publish(run.SpaceId, run.PrincipalUserId, new UserSignal(UserSignalKinds.RunFinished, run.ThreadId, run.Id));
        push.RunEnded(run.SpaceId, run.PrincipalUserId, run.BotId, run.ThreadId, status);

        logger.LogWarning("Run {RunId} ended as {Status} before it could start", run.Id, status);
    }

    private async Task<LoadedRun?> Load(IServiceProvider services, Guid runId, string workerId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<RunRepository>().FindRun(runId, cancellationToken);

        if (run is not { Status: RunStatus.Running } || run.LeaseOwner != workerId)
        {
            return null;
        }

        return await LoadRun(services, run, cancellationToken);
    }

    /// <summary>Everything a run needs to execute, read in the caller's transaction as the run's own person.</summary>
    private async Task<LoadedRun> LoadRun(IServiceProvider services, Run run, CancellationToken cancellationToken)
    {
        var runId = run.Id;
        var thread = await services.GetRequiredService<ThreadRepository>().FindThread(run.ThreadId, cancellationToken);
        var bot = await services.GetRequiredService<BotRepository>().FindBot(run.BotId, cancellationToken);
        var connections = services.GetRequiredService<ModelConnectionRepository>();
        var connection = bot is null ? null : await connections.FindConnection(bot.ModelConnectionId, cancellationToken);
        var entry = bot is null ? null : await connections.FindCatalogEntry(bot.ModelConnectionId, bot.Model, cancellationToken);
        var key = await RevealKey(services, connection, cancellationToken);
        var policies = services.GetRequiredService<PolicyService>();
        var policy = await policies.GetEffective(run.SpaceId, cancellationToken);
        var limits = (await policies.GetRuns(run.SpaceId, cancellationToken)).NarrowedBy(bot?.RunLimits ?? new RunLimits());
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

        // A bot that may delegate is told which bots it may hand work to: those directly beneath it.
        var children = bot is not null && granted.Any(tool => tool.Tool is { SourceKind: ToolSourceKind.Builtin, Name: DelegateTool.Name })
            ? await services.GetRequiredService<BotRepository>().ListChildren(bot.Id, cancellationToken)
            : [];

        // The notes the bot keeps about the run's person, read in this transaction, as that person. Whether the run is
        // given them is decided once its tools are known: only a run offered a memory tool is.
        var memory = bot is null || thread is null
            ? null
            : await services.GetRequiredService<MemoryService>().Recall(bot, thread, run.PrincipalUserId, cancellationToken);

        var carriesFiles = thread is not null
            && await services.GetRequiredService<FileRepository>().AnySentInThread(thread.Id, cancellationToken);

        return new LoadedRun(run, thread, bot, connection, key.Value, key.Readable, policy, limits, entry?.Pricing ?? new ModelPricing(), granted, steps, openMessage, unsettled, rows, answers, [.. delivered.Select(message => message.Id)], requested, toolConnections, entry?.Capabilities.Vision ?? false, memory, children, carriesFiles);
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

    private RunSession NewSession(LoadedRun loaded, RunTenant tenant, string workerId, RunSession? parent = null)
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
            parent?.Fence ?? new RunFence(workerId, loaded.Run.Attempt));
        // Two tools never share the name the model sees: the first keeps it, and a later one is left out.
        List<RunTool> tools =
        [
            .. loaded.Granted
                .Where(granted => granted.Effect is not GrantEffect.Deny && granted.Tool.State is AvailabilityState.Available)
                .Select(granted => ToRunTool(granted, loaded))
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
            loaded.Limits,
            loaded.Steps.Count,
            loaded.Delivered,
            loaded.Policy.TaintedRunEscalation,
            parent);
    }

    /// <summary>
    /// A granted tool as this run uses it. A built-in tool needs its code on this server; a sandbox tool also needs a
    /// bot with a sandbox, and a computer tool a sandbox with a screen and a model that sees images or a model set to
    /// read the screen for the bot, a crew tool a server that lets bots build bots, and a memory tool a bot with memory
    /// on in a conversation private to the run's person. A server's tool needs tool connections allowed on this server and its own connection switched on, and is named for the model
    /// with the connection's own prefix, so no server can take another's names or a built-in's.
    /// </summary>
    private RunTool? ToRunTool(GrantedTool granted, LoadedRun loaded)
    {
        var (bot, toolConnections, modelSeesImages) = (loaded.Bot, loaded.ToolConnections, loaded.ModelSeesImages);

        if (granted.Tool.SourceKind is ToolSourceKind.Builtin)
        {
            var builtin = builtins.FirstOrDefault(found => found.Name == granted.Tool.Name);
            var hasSandbox = builtin is not null && bot?.SandboxPolicy is not null and not SandboxPolicy.None;

            // A sandbox tool needs a sandbox provider on this server (its code is registered only then) and a bot whose
            // sandbox policy gives it a sandbox. Without both the model is not offered it, whatever the grant says.
            if (SandboxTools.IsSandboxTool(granted.Tool.Name) && !hasSandbox)
            {
                return null;
            }

            // A computer tool also needs a screen the deployment allows and something that can read a screenshot: the
            // bot's own model, or a model set to read the screen for it. Without either the bot would click blind.
            if (ComputerTools.IsComputerTool(granted.Tool.Name)
                && !(hasSandbox
                    && bot!.SandboxSettings.Desktop
                    && deployment.Value.Rules.AllowDesktopSandboxes
                    && (modelSeesImages || bot.SandboxSettings.ScreenReader is not null)))
            {
                return null;
            }

            // Where the server does not let bots build bots, the crew tools are not offered, whatever the grant says.
            if (CrewTools.IsCrewTool(granted.Tool.Name) && !deployment.Value.Rules.AllowBotsBuildingBots)
            {
                return null;
            }

            // A memory tool needs memory on for the bot and a conversation private to the run's person: a note is
            // that person's own, so it is never kept from a conversation other people read.
            if (MemoryTools.IsMemoryTool(granted.Tool.Name)
                && !(bot is { MemorySettings.Enabled: true } && loaded.Thread?.AudienceUserId == loaded.Run.PrincipalUserId))
            {
                return null;
            }

            return new RunTool(granted.Tool, granted.Effect, BuiltinTool.ModelName(granted.Tool.Name), builtin);
        }

        // With tool connections turned off on this server, a tool of one cannot be called, so the model is not offered it.
        return deployment.Value.Rules.AllowToolConnections
            && toolConnections.FirstOrDefault(found => found.Id == granted.Tool.ToolConnectionId) is { Status: not ConnectionStatus.Disabled } connection
            ? new RunTool(granted.Tool, granted.Effect, ToolNames.ForModel(connection.Slug, granted.Tool.Name), null, connection)
            : null;
    }

    /// <summary>
    /// The files people attached, as this run gives them to its model: by whether the model reads pictures and
    /// whether the run was given a sandbox tool, which takes a sandbox on this server, on the bot, and in its grants.
    /// </summary>
    private RunFiles NewRunFiles(RunSession session, LoadedRun loaded)
    {
        var hasSandbox = loaded.Granted.Any(granted => SandboxTools.IsSandboxTool(granted.Tool.Name)
            && session.FindTool(granted.Tool.Id) is not null);
        return new RunFiles(
            session.Tenant,
            loaded.Bot!.Id,
            loaded.ModelSeesImages,
            hasSandbox,
            deployment.Value.Rules.MaxDocumentPages,
            fileStore,
            sandboxBrokers.FirstOrDefault(),
            session.Taint,
            loggers.CreateLogger<RunFiles>());
    }

    #endregion

    #region Running

    /// <summary>
    /// Runs the agent and ends the run. Returns the status it ended or waits with, or null when it was left for
    /// another execution. <paramref name="stopping"/> stops the agent; <paramref name="serverStopping"/> is the server
    /// shutting down, which leaves the run as it is. For a root they are the same token; a child also stops with its
    /// parent.
    /// </summary>
    private async Task<RunStatus?> Run(RunSession session, LoadedRun loaded, CancellationToken stopping, CancellationToken serverStopping)
    {
        if (loaded.Thread is null)
        {
            await session.Tenant.Transact(
                services => SetStatus(services, session, RunStatus.Failed, null, new RunError("thread_not_visible", "The run's thread is no longer visible to the person it acts for.")),
                CancellationToken.None);
            logger.LogWarning("Run {RunId} failed: its thread is not visible to its person", session.RunId);
            return RunStatus.Failed;
        }

        var placed = await session.Tenant.Transact(
            services => services.GetRequiredService<ThreadLine>().PlaceMessagesOf(loaded.Run, loaded.Thread, CancellationToken.None),
            CancellationToken.None);
        await session.Writer.Write(null, [.. RecoveryEvents(session, loaded), .. session.Delivered(placed)]);

        if (FindReasonNotToStart(loaded) is { } refusal)
        {
            await Finish(session, refusal.Status, refusal.Reason, refusal.Error);
            return refusal.Status;
        }

        await TaintFromNotes(session, loaded);
        await TaintFromFiles(session, loaded);
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
            loggers.CreateLogger<RunGovernor>(),
            loaded.ModelSeesImages,
            NewRunFiles(session, loaded),
            new ChildRuns(this, session, serverStopping));
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
                return await FinishAfterFailure(session, new OperationCanceledException(), serverStopping);
            }

            var outcome = await DriveToAStop(session, loaded, model, governor, rules, cancellation.Token);

            // A rule can end the run while its last model call still returns normally.
            if (session.EndedByRule is not null)
            {
                return await FinishAfterFailure(session, new OperationCanceledException(), serverStopping);
            }

            var status = outcome is RunOutcome.Waiting ? RunStatus.Waiting : RunStatus.Completed;
            await Finish(session, status, status is RunStatus.Waiting ? WaitingReason(session) : null, null);
            return status;
        }
        catch (Exception exception) when (exception is not LeaseLostException)
        {
            logger.LogDebug(exception, "Run {RunId} stopped before the agent finished", session.RunId);
            return await FinishAfterFailure(session, exception, serverStopping);
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
                // A result the framework made from a stale session for a call that is to be asked again is not the
                // call's end: the call runs again, and its own result ends it.
                case ToolCallFinished finished when !session.AwaitsRerun(finished.CallId):
                    await session.CompleteTool(finished.CallId, new ToolOutcome(finished.Result, finished.IsError));
                    break;
                case ApprovalRequested approval:
                    await session.RequestTool(approval.CallId, approval.ToolName, approval.Arguments);
                    await governor.RuleOnApprovalRequest(approval.CallId, approval.ToolName, approval.Arguments, cancellationToken);

                    // A call the rules will refuse holds nothing: only one that can run waits for a person at the screen.
                    if (!await governor.RulesRefuse(approval.CallId, approval.ToolName, approval.Arguments, cancellationToken))
                    {
                        session.NoteWaitFor(approval.ToolName);
                    }

                    var details = await governor.DescribeForApproval(approval.CallId, approval.ToolName, approval.Arguments, cancellationToken);
                    await session.Writer.Append(
                        session.RunId,
                        RunEventTypes.ToolApprovalRequested,
                        new ToolApprovalRequested(ToolCallIds.For(session.RunId, approval.CallId), approval.RequestId, approval.CallId, details));
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
            Compaction = loaded.Bot.MemorySettings.Compaction,
        },
        Tools = [.. loaded.Granted.Select(granted => session.FindTool(granted.Tool.Id)).OfType<RunTool>().Select(tool => ToDefinition(session, loaded, tool))],
        Model = model,
        Memory = OfferedMemoryTools(session, loaded) is { Count: > 0 } offered && loaded.Memory is { } memory
            ? memory with { CanList = offered.Contains("memory.list") }
            : null,
    };

    /// <summary>The memory tools the run is offered. A bot that cannot keep notes is given no notes.</summary>
    private static List<string> OfferedMemoryTools(RunSession session, LoadedRun loaded) =>
    [
        .. loaded.Granted
            .Select(granted => session.FindTool(granted.Tool.Id))
            .OfType<RunTool>()
            .Where(tool => tool.Builtin is not null && MemoryTools.IsMemoryTool(tool.Tool.Name))
            .Select(tool => tool.Tool.Name),
    ];

    /// <summary>
    /// A run given a note that a tainted run wrote may be given that run's untrusted text, so it is tainted before its
    /// agent is built: its consequential tools ask from the first call, as they would in the run that read the text.
    /// </summary>
    private static async Task TaintFromNotes(RunSession session, LoadedRun loaded)
    {
        if (loaded.Memory is { Tainted: true } && OfferedMemoryTools(session, loaded).Count > 0)
        {
            await session.Taint();
        }
    }

    /// <summary>
    /// A file a person attached is text from outside Drover, a document or a picture of anything, so a run whose
    /// conversation carries one is tainted before its agent is built, as a run given a tool's result would be once it
    /// had read it: its consequential tools ask from the first call. Every later run of the conversation is given
    /// the file again, so it is tainted the same way.
    /// </summary>
    private static async Task TaintFromFiles(RunSession session, LoadedRun loaded)
    {
        if (loaded.CarriesFiles)
        {
            await session.Taint();
        }
    }

    private static ToolDefinition ToDefinition(RunSession session, LoadedRun loaded, RunTool tool) => new()
    {
        ToolId = tool.Tool.Id,
        Name = tool.ModelName,
        Description = tool.IsDelegation ? DelegateTool.DescribeFor(loaded.Children) : tool.Tool.Description,
        IsDelegation = tool.IsDelegation,
        IsIdempotent = tool.Builtin is { IsIdempotent: true },
        InputSchema = tool.Tool.InputSchema,
        DefinitionHash = tool.Tool.DefinitionHash,
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
                ? RunEventWriter.Event(run.Id, RunEventTypes.MessageCompleted, new MessageCompleted(stored.ToResponse(runPrincipalUserId: run.PrincipalUserId)))
                : RunEventWriter.Event(run.Id, RunEventTypes.StepDiscarded, open);
        }

        foreach (var callId in loaded.UnsettledCalls)
        {
            var row = loaded.UnsettledRows.FirstOrDefault(call => call.Id == callId);

            if (row?.Status is ToolCallStatus.PendingApproval or ToolCallStatus.Approved)
            {
                continue;
            }

            // A read-only or idempotent call runs again. A delegation is asked again, and resolves to the same child run.
            if (row is { Status: ToolCallStatus.Running } && session.FindTool(row.ToolId) is { MayRunAgain: true })
            {
                logger.LogInformation("Tool call {ToolCallId} of run {RunId} was cut off and will be asked again", callId, run.Id);
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

    private static string WaitingReason(RunSession session) =>
        session.WaitsForAPersonAtTheScreen ? RunReasons.AwaitingTakeover : RunReasons.AwaitingApproval;

    private static Refusal Failed(string code, string message) => new(RunStatus.Failed, null, new RunError(code, message));

    #endregion

    #region Finishing

    /// <summary>
    /// Ends a run the agent did not finish, with the status the reason calls for, and returns it. Null when the run
    /// is left as it is for another execution: the server is stopping. A run whose lease another claim holds now
    /// writes nothing at all.
    /// </summary>
    private async Task<RunStatus?> FinishAfterFailure(RunSession session, Exception failure, CancellationToken stopping)
    {
        if (session.LeaseLost || IsLeaseLostAbove(session) || Find<LeaseLostException>(failure) is not null)
        {
            throw new LeaseLostException(session.RunId);
        }

        var (status, reason, error) = WhyItStopped(session, failure, stopping);

        if (status is null)
        {
            await LeaveForAnotherExecution(session);
            return null;
        }

        await Finish(session, status.Value, reason, error);
        return status;
    }

    private (RunStatus? Status, string? Reason, RunError? Error) WhyItStopped(RunSession session, Exception failure, CancellationToken stopping)
    {
        if (session.CancelledByUser)
        {
            return (RunStatus.Cancelled, IsCancelledAbove(session) ? RunReasons.CancelledByParent : RunReasons.CancelledByUser, null);
        }

        // A child stops with the run that delegated to it, when that run was cancelled or a rule ended it.
        if (IsCancelledAbove(session))
        {
            return (RunStatus.Cancelled, RunReasons.CancelledByParent, null);
        }

        if (session.EndedByRule is { } rule)
        {
            return (RunStatus.Failed, rule.Reason, new RunError(rule.Reason, rule.Message));
        }

        // A run beneath this one waits for a person, so this one waits with it.
        if (session.PausedForChild)
        {
            return (RunStatus.Waiting, WaitingReason(session), null);
        }

        if (stopping.IsCancellationRequested && failure is OperationCanceledException)
        {
            return (null, null, null);
        }

        if (Find<RunLimitException>(failure) is { } limit)
        {
            return (RunStatus.Failed, limit.Reason, new RunError(limit.Reason, limit.Message));
        }

        if (Find<ModelProviderException>(failure) is { } provider)
        {
            logger.LogWarning("Run {RunId} failed at the model provider: {Failure}", session.RunId, provider.Failure);
            return (RunStatus.Failed, null, new RunError(provider.Failure.ToWireName(), provider.Message));
        }

        logger.LogError(failure, "Run {RunId} failed", session.RunId);
        return (RunStatus.Failed, null, new RunError("internal", "The run failed unexpectedly. The server log has the details."));
    }

    /// <summary>The server is stopping. A root's lease is released for the next server; a child is left to its root.</summary>
    private Task LeaveForAnotherExecution(RunSession session) =>
        session.Parent is null ? ReleaseLease(session) : Task.CompletedTask;

    private static bool IsCancelledAbove(RunSession session) =>
        session.Parent is { } parent && (parent.CancelledByUser || parent.EndedByRule is not null || IsCancelledAbove(parent));

    private static bool IsLeaseLostAbove(RunSession session) =>
        session.Parent is { } parent && (parent.LeaseLost || IsLeaseLostAbove(parent));

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
        IReadOnlyList<RunEvent> swept = [];
        await session.Writer.Write(
            async services => ended = await SetStatus(services, session, status, reason, error),
            [.. closing, RunEventWriter.Event(session.RunId, EventTypeFor(status), changed)],
            async services =>
            {
                if (ended is not { } over || status is RunStatus.Waiting)
                {
                    return;
                }

                var line = services.GetRequiredService<ThreadLine>();
                swept = await line.EndOpenDescendants(over.Run, CancellationToken.None);
                next = await line.RunEnded(over.Run, over.Thread, CancellationToken.None);
            });
        logger.LogInformation("Run {RunId} is {Status} ({Reason})", session.RunId, status, reason);
        hub.Publish(session.RootRunId, swept);

        if (next is not null)
        {
            hub.Publish(next.Run.RootRunId, next.Events);
            signals.Publish(next.Run.SpaceId, next.Run.PrincipalUserId, new UserSignal(UserSignalKinds.ThreadChanged, next.Run.ThreadId, next.Run.Id));
        }

        // A slot is free and the thread can take its next message.
        signal.Wake();

        // A child's thread is not one its person watches: what they need to know surfaces on the root.
        if (session.Parent is not null)
        {
            return;
        }

        signals.Publish(
            session.Tenant.SpaceId,
            session.Tenant.UserId,
            new UserSignal(
                status is RunStatus.Waiting ? UserSignalKinds.ApprovalWaiting : UserSignalKinds.RunFinished,
                session.Thread.Id,
                session.RunId));

        // Only the worker whose status change was written wakes the person, so one end is one push.
        if (ended is not null)
        {
            push.RunEnded(session.Tenant.SpaceId, session.Tenant.UserId, session.BotId, session.Thread.Id, status);
        }
    }

    /// <summary>
    /// The fenced status change. Returns the run and its thread, both locked, or null when the run's person cannot
    /// see the thread any more. The run row is locked first and the thread second, as everywhere a run is ended.
    /// </summary>
    private async Task<(Run Run, ChatThread Thread)?> SetStatus(IServiceProvider services, RunSession session, RunStatus status, string? reason, RunError? error)
    {
        var runs = services.GetRequiredService<RunRepository>();
        var run = await LockForEnding(runs, session);
        var now = time.GetUtcNow();
        run.Status = status;
        run.StatusReason = reason;
        run.Error = error;
        run.LeaseOwner = null;
        run.LeaseExpiresAt = null;
        run.Usage = session.Usage;

        if (status is RunStatus.Waiting)
        {
            run.WaitingSince = now;
        }
        else
        {
            run.FinishedAt = now;

            // A run that has ended adds what it used to its own subtree and to every run above it, once.
            await runs.AddToSubtreeUsage(run.Id, run.Usage, CancellationToken.None);
        }

        if (await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(run.ThreadId, CancellationToken.None) is not { } thread)
        {
            return null;
        }

        thread.LastActivityAt = now;
        return (run, thread);
    }

    /// <summary>
    /// The run row, locked, after checking this execution still holds the lease that covers it. A root holds its own;
    /// a child is covered by its root's, which is locked first, the order every writer of a tree takes its rows in.
    /// </summary>
    private static async Task<Run> LockForEnding(RunRepository runs, RunSession session)
    {
        if (session.Parent is not null && !IsHeld(await runs.GetRunForUpdate(session.LeaseRunId, CancellationToken.None), session.Fence))
        {
            throw new LeaseLostException(session.RunId);
        }

        var run = await runs.GetRunForUpdate(session.RunId, CancellationToken.None);
        var held = session.Parent is null ? IsHeld(run, session.Fence) : run is { Status: RunStatus.Running };
        return held ? run! : throw new LeaseLostException(session.RunId);
    }

    private static bool IsHeld(Run? run, RunFence fence) =>
        run is { Status: RunStatus.Running } && run.LeaseOwner == fence.Worker && run.Attempt == fence.Attempt;

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

                // A child's lease is its root's, which the root's own watch renews.
                if (renew && session.Parent is null && !await runs.RenewLease(session.RunId, session.Fence, cancellationToken))
                {
                    return new LeaseState(false, false);
                }

                return await runs.ReadLease(session.LeaseRunId, session.RunId, session.Fence, cancellationToken);
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
        RunCeilings Limits,
        ModelPricing Pricing,
        IReadOnlyList<GrantedTool> Granted,
        StepState Steps,
        Message? OpenStepMessage,
        IReadOnlyList<Guid> UnsettledCalls,
        IReadOnlyList<ToolCall> UnsettledRows,
        IReadOnlyList<PendingResponse> Answers,
        IReadOnlyList<Guid> Delivered,
        IReadOnlyList<Guid> RequestedCalls,
        IReadOnlyList<ToolConnection> ToolConnections,
        bool ModelSeesImages,
        RecalledMemory? Memory,
        IReadOnlyList<Bot> Children,
        bool CarriesFiles);

    #endregion
}
