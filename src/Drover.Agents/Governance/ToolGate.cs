namespace Drover.Agents.Governance;

/// <summary>
/// Function middleware every tool call passes through, including calls to tools a framework capability added. It
/// asks the engine's governor, keeps the <c>tool_calls</c> row, and orders calls: read-only tools requested together
/// run in parallel, every other call waits for the non-read-only calls the model listed before it.
/// </summary>
public sealed class ToolGate(RunContext run, ILogger logger)
{
    #region Private Fields

    private const string ApprovalUnavailable =
        "This call needs a human's approval, and approval can only be asked for before the run starts calling tools.";

    #endregion

    #region Public Methods

    public async ValueTask<object?> Invoke(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var callId = context.CallContent.CallId;

        try
        {
            await WaitForEarlierCalls(context);
            return await Decide(context, next, cancellationToken);
        }
        finally
        {
            run.MarkFinished(callId);
        }
    }

    #endregion

    #region Gate

    private async ValueTask<object?> Decide(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(context);
        var decision = await run.Governor.Decide(request, cancellationToken);
        var verdict = decision.Verdict is ToolGateVerdict.Ask && request.Approved ? ToolGateVerdict.Run : decision.Verdict;

        if (verdict is not ToolGateVerdict.Run)
        {
            var reason = verdict is ToolGateVerdict.Ask ? ApprovalUnavailable : decision.Reason ?? "Refused.";
            logger.LogInformation("Tool {ToolName} refused in run {RunId}", request.ToolName, request.RunId);
            return await Finish(request, ToolCallStatus.Failed, new ToolOutcome(MessageParts.ToJson(new { error = reason }), true), cancellationToken);
        }

        await Record(request, ToolCallStatus.Running, null, cancellationToken);

        try
        {
            var result = await next(context, cancellationToken);
            var outcome = result as ToolOutcome ?? new ToolOutcome(MessageParts.ToJson(result));
            return await Finish(request, outcome.IsError ? ToolCallStatus.Failed : ToolCallStatus.Completed, outcome, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed in run {RunId}", request.ToolName, request.RunId);
            await Finish(request, ToolCallStatus.Failed, new ToolOutcome(MessageParts.ToJson(new { error = "The tool failed." }), true), cancellationToken);
            throw;
        }
    }

    private ToolCallRequest BuildRequest(FunctionInvocationContext context) => new()
    {
        RunId = run.Execution.RunId,
        CallId = context.CallContent.CallId,
        ToolName = context.Function.Name,
        ToolId = run.FindTool(context.Function.Name)?.ToolId,
        Arguments = MessageParts.ToJson(context.Arguments),

        // The framework invokes an approval-required function only after a human approved the call.
        Approved = context.Function.GetService<ApprovalRequiredAIFunction>() is not null,
    };

    private async Task<JsonElement> Finish(
        ToolCallRequest request,
        ToolCallStatus status,
        ToolOutcome outcome,
        CancellationToken cancellationToken)
    {
        run.RecordOutcome(request.CallId, outcome.IsError);
        await Record(request, status, outcome, cancellationToken);
        return outcome.Result;
    }

    #endregion

    #region Ordering

    /// <summary>
    /// The framework's function context carries no conversation when history is persisted per service call, so the
    /// batch a call belongs to comes from the run, which the history provider tells about every response it stores.
    /// </summary>
    private async Task WaitForEarlierCalls(FunctionInvocationContext context)
    {
        if (run.IsReadOnly(context.Function.Name))
        {
            return;
        }

        var callId = context.CallContent.CallId;
        var earlier = run.CallsRequestedBefore(callId).Where(call => !run.IsReadOnly(call.Name));

        await Task.WhenAll(earlier.Select(call => run.Finished(call.CallId)));
    }

    #endregion

    #region Bookkeeping

    /// <summary>Keeps the row of a catalog tool. A tool outside the catalog has no row until Phase 1 gives it one.</summary>
    private Task Record(
        ToolCallRequest request,
        ToolCallStatus status,
        ToolOutcome? outcome,
        CancellationToken cancellationToken)
    {
        if (request.ToolId is not { } toolId)
        {
            return Task.CompletedTask;
        }

        return run.Database.Transact(
            async services =>
            {
                var calls = services.GetRequiredService<ToolCallRepository>();
                var id = ToolCallIds.For(request.RunId, request.CallId);
                var row = await calls.GetToolCall(id, cancellationToken);

                if (row is null)
                {
                    row = run.NewToolCallRow(id, toolId, request.ToolName, request.Arguments);
                    calls.AddToolCall(row);
                }

                row.Status = status;
                row.StartedAt ??= DateTimeOffset.UtcNow;

                if (outcome is not null)
                {
                    row.Result = outcome.Result;
                    row.IsError = outcome.IsError;
                    row.FinishedAt = DateTimeOffset.UtcNow;
                }
            },
            cancellationToken);
    }

    #endregion
}
