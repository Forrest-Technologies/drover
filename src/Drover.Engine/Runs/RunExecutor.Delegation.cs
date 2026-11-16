namespace Drover.Engine.Runs;

/// <summary>
/// Delegation: a run hands a task to a bot directly beneath its own, which works it as a child run. The child is a
/// row of its own, with its own thread, grants, history, session, status, usage, and events, executed here by the
/// worker that holds the tree's root, inside the parent's tool call. Nothing in memory is needed after a restart:
/// the parent's call is asked again and resolves to the same child run, which goes on from its own rows.
/// </summary>
public sealed partial class RunExecutor
{
    #region Delegation

    /// <summary>
    /// Begins a child run for a parent's <c>delegate</c> call, in one transaction as the run's person, or finds the one
    /// already begun for that call. The checks come first and the first that fails is the answer: the grant, the
    /// target, a loop, and the tree's budget.
    /// </summary>
    private async Task<ChildRun> BeginChild(RunSession parent, DelegationRequest request, Guid delegateToolId, CancellationToken cancellationToken)
    {
        var callRowId = ToolCallIds.For(parent.RunId, request.ParentCallId!);
        return await parent.Writer.Write(async (services, produced) =>
        {
            var calls = services.GetRequiredService<ToolCallRepository>();

            if (await calls.GetToolCallForUpdate(callRowId, cancellationToken) is not { } call)
            {
                return Refused(request, "unavailable", "This call was not recorded, so nothing was handed over.");
            }

            if (call.ChildRunId is { } existing)
            {
                return await FindChild(services, request, existing, cancellationToken);
            }

            var (target, refusal) = await CheckDelegation(services, parent, request, delegateToolId, cancellationToken);

            if (refusal is not null)
            {
                return refusal;
            }

            var child = await AddChild(services, parent, request, target!, callRowId, cancellationToken);
            call.ChildRunId = child.Id;
            produced.Add(RunEventWriter.Event(parent.RunId, RunEventTypes.DelegationStarted, new DelegationStarted(callRowId, child.Id, target!.Id)));
            logger.LogInformation("Run {RunId} handed a task to bot {BotId} as child run {ChildRunId}", parent.RunId, target.Id, child.Id);
            return new ChildRun(child.Id, target.Name) { BotId = target.Id };
        });
    }

    private static async Task<ChildRun> FindChild(IServiceProvider services, DelegationRequest request, Guid childRunId, CancellationToken cancellationToken)
    {
        var child = await services.GetRequiredService<RunRepository>().FindRun(childRunId, cancellationToken);
        var bot = child is null ? null : await services.GetRequiredService<BotRepository>().FindBot(child.BotId, cancellationToken);
        return child is null
            ? Refused(request, "unavailable", "The bot this task was handed to can no longer be reached.")
            : new ChildRun(child.Id, bot?.Name ?? request.AgentName) { BotId = child.BotId };
    }

    private async Task<(Bot? Target, ChildRun? Refusal)> CheckDelegation(
        IServiceProvider services,
        RunSession parent,
        DelegationRequest request,
        Guid delegateToolId,
        CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();
        var caller = await bots.FindBot(parent.BotId, cancellationToken);
        var grant = await services.GetRequiredService<ToolRepository>().FindEffectiveGrant(parent.BotId, delegateToolId, cancellationToken);

        if (caller is not { Status: BotStatus.Active } || grant is not (GrantEffect.Allow or GrantEffect.Ask))
        {
            return (null, Refused(request, "no_grant", "This bot may not hand work to other bots."));
        }

        var target = request.BotId is { } targetId ? await bots.FindBot(targetId, cancellationToken) : null;

        if (target is null || target.ParentBotId != caller.Id)
        {
            return (null, Refused(request, "not_beneath", "That is not a bot directly beneath this one. Hand work only to the bots this tool lists."));
        }

        if (target.Status is not BotStatus.Active)
        {
            var state = target.Status is BotStatus.Paused ? "is paused" : "was removed";
            return (null, Refused(request, "target_unavailable", $"{target.Name} {state}, so it cannot take the task."));
        }

        var runs = services.GetRequiredService<RunRepository>();

        // The root row is locked before anything about the tree is counted, so two delegations in one tree take turns
        // and cannot both fit under a limit only one of them fits under.
        await runs.GetRunForUpdate(parent.RootRunId, cancellationToken);

        // Down the tree only, so a loop cannot happen while the tree stands still. The run's own ancestors are the
        // guard for a tree that changed while the chain was working.
        if ((await runs.ListSelfAndAncestors(parent.RunId, cancellationToken)).Any(run => run.BotId == target.Id))
        {
            return (null, Refused(request, RunReasons.LoopDetected, $"{target.Name} is already working on this chain of tasks, above this bot. Handing it this task would make a loop."));
        }

        var limits = await services.GetRequiredService<PolicyService>().GetDelegation(parent.Tenant.SpaceId, cancellationToken);
        return (target, await CheckBudget(runs, parent, request, limits, cancellationToken));
    }

    private static async Task<ChildRun?> CheckBudget(RunRepository runs, RunSession parent, DelegationRequest request, DelegationLimits limits, CancellationToken cancellationToken)
    {
        if (parent.Depth + 1 > limits.MaxDepth)
        {
            return OverBudget(request, $"This chain of tasks is as deep as it may go: {nameof(DeploymentPolicy.MaxDelegationDepth)} is {limits.MaxDepth}.");
        }

        if (await runs.CountRunsInTree(parent.RootRunId, cancellationToken) + 1 > limits.MaxRuns)
        {
            return OverBudget(request, $"This chain of tasks has as many runs as it may have: {nameof(DeploymentPolicy.MaxRunsPerDelegationTree)} is {limits.MaxRuns}.");
        }

        var cost = await runs.SumTreeCost(parent.RootRunId, cancellationToken);

        return cost >= limits.MaxCost
            ? OverBudget(request, $"This chain of tasks has spent its budget: it cost {cost:0.00}, and {nameof(DeploymentPolicy.MaxDelegationTreeCostCents)} is {limits.MaxCostCents}.")
            : null;
    }

    /// <summary>
    /// The child's thread, its first message (the task), and its run. The thread has the audience of the tree's root
    /// thread, so exactly the people who can see the root can see it. The run acts for the same person, starts
    /// working at once inside the parent's call, and starts tainted when its parent is.
    /// </summary>
    private async Task<Run> AddChild(IServiceProvider services, RunSession parent, DelegationRequest request, Bot target, Guid callRowId, CancellationToken cancellationToken)
    {
        var runs = services.GetRequiredService<RunRepository>();
        var root = await runs.FindRun(parent.RootRunId, cancellationToken)
            ?? throw new InvalidOperationException($"The root of run {parent.RunId} is not visible to its person.");
        var threads = services.GetRequiredService<ThreadRepository>();
        var rootThread = await threads.FindThread(root.ThreadId, cancellationToken)
            ?? throw new InvalidOperationException($"The thread of root run {root.Id} is not visible to its person.");
        var now = time.GetUtcNow();
        var thread = new ChatThread
        {
            BotId = target.Id,
            OwnerUserId = rootThread.OwnerUserId,
            AudienceUserId = rootThread.AudienceUserId,
            Origin = ThreadOrigin.Delegation,
            Title = ThreadService.Shorten(parent.Redactor.Redact(request.Task)),
            LastActivityAt = now,
        };
        threads.Add(thread);
        var child = new Run
        {
            ThreadId = thread.Id,
            BotId = target.Id,
            PrincipalUserId = parent.Tenant.UserId,
            Trigger = RunTrigger.Delegation,
            ParentRunId = parent.RunId,
            RootRunId = parent.RootRunId,
            ParentToolCallId = callRowId,
            Depth = parent.Depth + 1,
            Status = RunStatus.Running,
            StartedAt = now,
            Tainted = parent.Tainted,
        };
        var task = new Message
        {
            Role = MessageRole.User,
            AuthorBotId = parent.BotId,
            RunId = child.Id,
            Parts = [new TextPart(parent.Redactor.Redact(request.Task))],
            CreatedAt = now,
        };
        child.TriggerMessageId = task.Id;
        runs.Add(child);
        await services.GetRequiredService<MessageRepository>().AppendMessages(thread, [task], cancellationToken);
        return child;
    }

    /// <summary>
    /// Executes a child run inside its parent's call until it ends or waits, and reports how it ended. A child that
    /// ended before (the parent's call was cut off after it) is not run again; its report is read from its rows.
    /// </summary>
    private async Task<ChildRunReport> ExecuteChild(RunSession parent, Guid childRunId, CancellationToken serverStopping, CancellationToken cancellationToken)
    {
        var tenant = parent.Tenant;
        ChildLoad? load;

        try
        {
            load = await tenant.Transact(services => LoadChild(services, parent, childRunId, cancellationToken), cancellationToken);
        }
        catch (LeaseLostException taken)
        {
            parent.LoseLease();
            throw new OperationCanceledException($"Run {parent.RootRunId} was taken over before child run {childRunId} could go on.", taken);
        }

        if (load?.Loaded is { } loaded)
        {
            await ExecuteChildSession(parent, loaded, serverStopping, cancellationToken);
        }

        return await tenant.Transact(services => ReadReport(services, childRunId, cancellationToken), CancellationToken.None);
    }

    private async Task ExecuteChildSession(RunSession parent, LoadedRun loaded, CancellationToken serverStopping, CancellationToken cancellationToken)
    {
        await using var session = NewSession(loaded, parent.Tenant, parent.Fence.Worker, parent);
        RunStatus? status;

        try
        {
            status = await ExecuteSession(session, loaded, cancellationToken, serverStopping);
        }
        catch (LeaseLostException taken)
        {
            parent.LoseLease();
            throw new OperationCanceledException($"Run {parent.RootRunId} was taken over while child run {loaded.Run.Id} worked.", taken);
        }

        // Left as it is: the server is stopping, and the root's next execution takes the child up again.
        if (status is null)
        {
            throw new OperationCanceledException($"Child run {loaded.Run.Id} was left for the next execution of its tree.");
        }
    }

    /// <summary>
    /// The child, made to work again under its root's lease, which must still be this execution's. A child that ended
    /// already is returned as it is, with nothing loaded.
    /// </summary>
    private async Task<ChildLoad?> LoadChild(IServiceProvider services, RunSession parent, Guid childRunId, CancellationToken cancellationToken)
    {
        var runs = services.GetRequiredService<RunRepository>();

        if (!IsHeld(await runs.GetRunForUpdate(parent.LeaseRunId, cancellationToken), parent.Fence))
        {
            throw new LeaseLostException(parent.RootRunId);
        }

        if (await runs.GetRunForUpdate(childRunId, cancellationToken) is not { } child)
        {
            return null;
        }

        if (child.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
        {
            return new ChildLoad(null);
        }

        child.Status = RunStatus.Running;
        child.StatusReason = null;
        child.StartedAt ??= time.GetUtcNow();
        return new ChildLoad(await LoadRun(services, child, cancellationToken));
    }

    /// <summary>How the child ended, and its last reply as its answer. A failure's answer is what went wrong.</summary>
    private static async Task<ChildRunReport> ReadReport(IServiceProvider services, Guid childRunId, CancellationToken cancellationToken)
    {
        var child = await services.GetRequiredService<RunRepository>().FindRun(childRunId, cancellationToken);
        return await ReadOutcome(services, child, cancellationToken) with { Tainted = child?.Tainted ?? false };
    }

    private static async Task<ChildRunReport> ReadOutcome(IServiceProvider services, Run? child, CancellationToken cancellationToken)
    {
        switch (child?.Status)
        {
            case RunStatus.Completed:
                var messages = await services.GetRequiredService<MessageRepository>().ListMessages(child!.ThreadId, cancellationToken);
                var answer = messages.LastOrDefault(message => message.Role is MessageRole.Assistant && message.RunId == child.Id);
                return new ChildRunReport(ChildRunOutcome.Completed, answer is null ? null : string.Concat(answer.Parts.OfType<TextPart>().Select(part => part.Text)));
            case RunStatus.Waiting:
                return new ChildRunReport(ChildRunOutcome.Waiting);
            case RunStatus.Cancelled:
                return new ChildRunReport(ChildRunOutcome.Cancelled);
            default:
                return new ChildRunReport(ChildRunOutcome.Failed, child?.Error?.Message ?? child?.StatusReason ?? "The run could not be found.");
        }
    }

    private static ChildRun Refused(DelegationRequest request, string reason, string message) =>
        new(Guid.Empty, request.AgentName)
        {
            BotId = request.BotId ?? Guid.Empty,
            Refusal = new ToolOutcome(JsonSerializer.SerializeToElement(new { error = message, reason }), true),
        };

    private static ChildRun OverBudget(DelegationRequest request, string message) => Refused(request, RunReasons.BudgetExceeded, message);

    #endregion

    #region Nested Types

    private sealed record ChildLoad(LoadedRun? Loaded);

    /// <summary>
    /// The delegations of one executing run: what its governor asks for when its bot calls <c>delegate</c>. It knows
    /// the run's session and the server's own stopping token, which a child needs to tell a shutdown from a cancel.
    /// </summary>
    public sealed class ChildRuns(RunExecutor executor, RunSession parent, CancellationToken serverStopping)
    {
        #region Private Fields

        private readonly ConcurrentDictionary<Guid, string> calls = new();

        #endregion

        #region Public Methods

        public async Task<ChildRun> Begin(DelegationRequest request, Guid delegateToolId, CancellationToken cancellationToken)
        {
            var child = await executor.BeginChild(parent, request, delegateToolId, cancellationToken);

            if (child.Refusal is null)
            {
                calls[child.RunId] = request.ParentCallId!;
            }

            return child;
        }

        public Task<ChildRunReport> Execute(ChildRun child, CancellationToken cancellationToken) =>
            executor.ExecuteChild(parent, child.RunId, serverStopping, cancellationToken);

        /// <summary>The parent's call, as the model named it, that began the child run.</summary>
        public string CallOf(ChildRun child) =>
            calls.TryGetValue(child.RunId, out var callId) ? callId : throw new InvalidOperationException($"Child run {child.RunId} was not begun by run {parent.RunId}.");

        /// <summary>Whether somebody asked the parent itself to stop, as the database says now.</summary>
        public Task<bool> IsParentCancelRequested() =>
            parent.Tenant.Transact(
                async services => (await services.GetRequiredService<RunRepository>().FindRun(parent.RunId, CancellationToken.None))?.CancelRequestedAt is not null,
                CancellationToken.None);

        #endregion
    }

    #endregion
}
