namespace Drover.Engine.Runs;

/// <summary>
/// The engine's side of one run. The runtime asks it before every tool call and hands it every call to execute, so
/// the model never decides what it may do: the grant is read again when the call is made, the bot must still be
/// active, and the result is redacted before anything stores or sees it.
/// </summary>
public sealed class RunGovernor(RunSession session, Bot bot, ModelPricing pricing, IToolSource? toolServers, ILogger logger) : IRunGovernor
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
        return (effect is { } granted ? session.EffectNow(tool, granted) : effect) switch
        {
            GrantEffect.Allow => ToolGateDecision.Run,
            GrantEffect.Ask => ToolGateDecision.Ask,
            _ => ToolGateDecision.Refuse($"This bot has no grant for the tool {toolCall.ToolName}."),
        };
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
            var result = await builtin.Run(toolCall.Arguments, cancellationToken);
            return new ToolOutcome(session.Redactor.Redact(result));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed in run {RunId}", toolCall.ToolName, session.RunId);
            return Error("The tool failed.");
        }
    }

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

    #region Private Methods

    private static ToolOutcome Error(string message) => new(JsonSerializer.SerializeToElement(new { error = message }), true);

    private decimal Cost(ModelUsage usage) =>
        ((usage.InputTokens * (pricing.InputPerMillion ?? 0)) + (usage.OutputTokens * (pricing.OutputPerMillion ?? 0))) / 1_000_000m;

    #endregion
}
