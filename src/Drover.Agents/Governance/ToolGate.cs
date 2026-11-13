namespace Drover.Agents.Governance;

/// <summary>
/// Function middleware every tool call passes through, including calls to tools a framework capability added. It
/// asks the engine's governor, keeps the <c>tool_calls</c> row through the ledger, and orders calls: read-only tools
/// requested together run in parallel, every other call waits for the non-read-only calls listed before it.
/// </summary>
public sealed class ToolGate(RunContext run, ILogger logger)
{
    #region Private Fields

    private const string ApprovalUnavailable =
        "This call needs a human's approval, and approval can only be asked for before the run starts calling tools.";

    private const string NotApproved = "This call needs a human's approval, and no approval for it is on record.";

    private readonly ToolCallLedger ledger = new(run);

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
        var definition = CatalogDefinition(context.Function);
        var wrapped = context.Function.GetService<ApprovalRequiredAIFunction>() is not null;
        var request = await BuildRequest(context, definition, wrapped, cancellationToken);
        var decision = await run.Governor.Decide(request, cancellationToken);

        if (decision.Arguments is { ValueKind: JsonValueKind.Object } ruled)
        {
            // A rule removed something from the arguments. The tool is called with what is left, and that is what
            // the call's row keeps.
            AIFunctionArguments left = [];

            foreach (var argument in ruled.EnumerateObject())
            {
                left[argument.Name] = argument.Value;
            }

            context.Arguments = left;
            request = request with { Arguments = ruled };
        }
        var verdict = decision.Verdict is ToolGateVerdict.Ask && request.Approved ? ToolGateVerdict.Run : decision.Verdict;

        if (verdict is not ToolGateVerdict.Run)
        {
            var reason = verdict is not ToolGateVerdict.Ask ? decision.Reason ?? "Refused." : wrapped ? NotApproved : ApprovalUnavailable;
            logger.LogInformation("Tool {ToolName} refused in run {RunId}", request.ToolName, request.RunId);
            return await Finish(request, definition, ToolCallStatus.Failed, ToolCallLedger.Error(reason), cancellationToken);
        }

        if (definition is not null && await ledger.Claim(request, definition.ToolId, cancellationToken) is { } settled)
        {
            logger.LogInformation("Tool call {CallId} in run {RunId} was settled before; not running it again", request.CallId, request.RunId);
            run.RecordOutcome(request.CallId, settled.IsError);
            return settled.Result;
        }

        try
        {
            var result = await next(context, cancellationToken);
            var outcome = result as ToolOutcome ?? new ToolOutcome(MessageParts.ToJson(result));
            return await Finish(request, definition, outcome.IsError ? ToolCallStatus.Failed : ToolCallStatus.Completed, outcome, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed in run {RunId}", request.ToolName, request.RunId);
            await Finish(request, definition, ToolCallStatus.Failed, ToolCallLedger.Error("The tool failed."), cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// The catalog tool behind a function, from the function object itself, never from its name. A function a
    /// capability added is not a catalog tool, whatever it is called.
    /// </summary>
    private static ToolDefinition? CatalogDefinition(AIFunction function) =>
        function.GetService<GovernedToolFunction>()?.Definition;

    private async Task<ToolCallRequest> BuildRequest(
        FunctionInvocationContext context,
        ToolDefinition? definition,
        bool wrapped,
        CancellationToken cancellationToken) => new()
        {
            RunId = run.Execution.RunId,
            CallId = context.CallContent.CallId,
            ToolName = context.Function.Name,
            ToolId = definition?.ToolId,
            Arguments = MessageParts.ToJson(context.Arguments),

            // The framework invokes an approval-required function only after it was handed an approval. That is not
            // taken on trust: the call's row must say a person approved it, so the row is the authority at call time.
            Approved = wrapped && await ledger.WasApprovedByAPerson(context.CallContent.CallId, cancellationToken),
        };

    private async Task<JsonElement> Finish(
        ToolCallRequest request,
        ToolDefinition? definition,
        ToolCallStatus status,
        ToolOutcome outcome,
        CancellationToken cancellationToken)
    {
        run.RecordOutcome(request.CallId, outcome.IsError);

        // A tool outside the catalog has no row until Phase 1 gives capability tools catalog entries.
        if (definition is not null)
        {
            await ledger.Finish(request, definition.ToolId, status, outcome, cancellationToken);
        }

        return outcome.Result;
    }

    #endregion

    #region Ordering

    /// <summary>
    /// The framework's function context carries no conversation when history is persisted per service call, so the
    /// batch a call belongs to comes from the run, which the history provider tells about every response it stores.
    /// Tool names are unique within a run (the runtime refuses duplicates), so a name identifies the tool here.
    /// </summary>
    private async Task WaitForEarlierCalls(FunctionInvocationContext context)
    {
        if (CatalogDefinition(context.Function)?.Sensitivity is ToolSensitivity.Read)
        {
            return;
        }

        var earlier = run.CallsRequestedBefore(context.CallContent.CallId).Where(call => !run.IsReadOnly(call.Name));
        await Task.WhenAll(earlier.Select(call => run.Finished(call.CallId)));
    }

    #endregion
}
