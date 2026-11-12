namespace Drover.Engine.Runs;

/// <summary>
/// The engine's side of one run. The runtime asks it before every tool call and hands it every call to execute, so
/// the model never decides what it may do: the grant is read again when the call is made, the bot must still be
/// active, and the result is redacted before anything stores or sees it.
/// </summary>
public sealed class RunGovernor(RunSession session, Bot bot, ModelPricing pricing, ILogger logger) : IRunGovernor
{
    #region Private Fields

    private const string NotAvailable = "Delegation is not available in Phase 0.";

    #endregion

    #region Interface Implementations

    public async Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        // The model call that asked for this tool is stored by now, so its message can be announced before the tool starts.
        await session.AnnounceStoredMessage();
        var decision = await DecideNow(toolCall, cancellationToken);

        if (decision.Verdict is ToolGateVerdict.Refuse)
        {
            session.MarkRefused(toolCall.CallId);
            logger.LogInformation("Tool {ToolName} refused in run {RunId}: {Reason}", toolCall.ToolName, session.RunId, decision.Reason);
        }

        if (decision.Verdict is not ToolGateVerdict.Run && session.IsRerun(toolCall.CallId))
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
            var outcome = await Run(toolCall, cancellationToken);
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

    public Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(NotAvailable);

    public Task CompleteChildRun(ChildRun child, ChildRunOutcome outcome, CancellationToken cancellationToken) =>
        throw new NotSupportedException(NotAvailable);

    public async Task RecordUsage(ModelUsage usage, CancellationToken cancellationToken)
    {
        var cost = Cost(usage);
        session.AddUsage(usage.InputTokens, usage.OutputTokens, cost);
        var recorded = new UsageRecorded(session.CurrentStep, usage.InputTokens, usage.OutputTokens, 0, cost, pricing.Currency);
        await session.Writer.Write(
            async services =>
            {
                var runs = services.GetRequiredService<RunRepository>();
                runs.Add(new UsageRecord
                {
                    RunId = session.RunId,
                    RootRunId = session.RootRunId,
                    BotId = bot.Id,
                    ModelConnectionId = bot.ModelConnectionId,
                    Model = bot.Model,
                    InputTokens = usage.InputTokens,
                    OutputTokens = usage.OutputTokens,
                    Cost = cost,
                    Currency = pricing.Currency,
                });

                if (await runs.GetRunForUpdate(session.RunId, CancellationToken.None) is { } run)
                {
                    run.Usage = session.Usage;
                }
            },
            [RunEventWriter.Event(session.RunId, RunEventTypes.UsageRecorded, recorded)]);
    }

    public JsonElement Redact(JsonElement value) => session.Redactor.Redact(value);

    #endregion

    #region Gate

    private async Task<ToolGateDecision> DecideNow(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (toolCall.ToolId is not { } toolId || session.FindTool(toolId) is null)
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

        return effect switch
        {
            GrantEffect.Allow => ToolGateDecision.Run,
            GrantEffect.Ask => ToolGateDecision.Ask,
            _ => ToolGateDecision.Refuse($"This bot has no grant for the tool {toolCall.ToolName}."),
        };
    }

    private async Task<ToolOutcome> Run(ToolCallRequest toolCall, CancellationToken cancellationToken)
    {
        if (toolCall.ToolId is not { } toolId || session.FindTool(toolId)?.Builtin is not { } builtin)
        {
            return Error("This server has nothing that runs that tool.");
        }

        try
        {
            var result = await builtin.Run(toolCall.Arguments, cancellationToken);
            return new ToolOutcome(session.Redactor.Redact(result));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed in run {RunId}", toolCall.ToolName, session.RunId);
            return Error("The tool failed.");
        }
    }

    private static ToolOutcome Error(string message) => new(JsonSerializer.SerializeToElement(new { error = message }), true);

    private decimal Cost(ModelUsage usage) =>
        ((usage.InputTokens * (pricing.InputPerMillion ?? 0)) + (usage.OutputTokens * (pricing.OutputPerMillion ?? 0))) / 1_000_000m;

    #endregion
}
