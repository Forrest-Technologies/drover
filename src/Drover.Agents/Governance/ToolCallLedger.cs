namespace Drover.Agents.Governance;

/// <summary>
/// Every decision about a <c>tool_calls</c> row in one place. The row is the record of what really happened to a
/// call, so when the row and anything else disagree (a framework-made result, a stale session) the row wins, and a
/// call the row says already started is never started again.
/// </summary>
public sealed class ToolCallLedger(RunContext run)
{
    #region Private Fields

    private const string InterruptedText = "interrupted, outcome unknown";

    #endregion

    #region Properties

    public static ToolOutcome Interrupted { get; } = new(MessageParts.ToJson(InterruptedText), true);

    #endregion

    #region Public Methods

    public static ToolOutcome Error(string message) => new(MessageParts.ToJson(new { error = message }), true);

    /// <summary>
    /// Claims a call before its tool runs. Null means run it. Otherwise the call was settled before, by this run in
    /// an earlier process: a finished call returns its stored outcome, and a call that was running when its process
    /// died is marked interrupted. Either way the tool does not run again.
    /// </summary>
    public Task<ToolOutcome?> Claim(ToolCallRequest request, Guid toolId, CancellationToken cancellationToken) =>
        run.Database.Transact(
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

                return Settle(row) ?? Start(row);
            },
            cancellationToken);

    /// <summary>
    /// True when the call's row records a person's approval: someone decided it, and not with a no. A row that moved
    /// on after the approval (it started, or finished) still counts, so a call a restart cut off reaches
    /// <see cref="Claim"/>, which then refuses to run it twice.
    /// </summary>
    public Task<bool> WasApprovedByAPerson(string callId, CancellationToken cancellationToken) =>
        run.Database.Transact(
            async services =>
            {
                var row = await services.GetRequiredService<ToolCallRepository>()
                    .GetToolCall(ToolCallIds.For(run.Execution.RunId, callId), cancellationToken);
                return row is { DecidedByUserId: not null, Status: not (ToolCallStatus.PendingApproval or ToolCallStatus.Denied) };
            },
            cancellationToken);

    public Task Finish(
        ToolCallRequest request,
        Guid toolId,
        ToolCallStatus status,
        ToolOutcome outcome,
        CancellationToken cancellationToken) =>
        run.Database.Transact(
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

                Complete(row, status, outcome);
            },
            cancellationToken);

    public Task RecordPendingApproval(FunctionCallContent call, ToolDefinition tool, CancellationToken cancellationToken) =>
        run.Database.Transact(
            async services =>
            {
                var calls = services.GetRequiredService<ToolCallRepository>();
                var id = ToolCallIds.For(run.Execution.RunId, call.CallId);

                if (await calls.GetToolCall(id, cancellationToken) is null)
                {
                    var row = run.NewToolCallRow(id, tool.ToolId, call.Name, MessageParts.ToJson(call.Arguments));
                    row.Status = ToolCallStatus.PendingApproval;
                    calls.AddToolCall(row);
                }
            },
            cancellationToken);

    /// <summary>
    /// The messages with every tool result replaced by its row's, where the row has one. A result the framework made
    /// for a call still waiting for approval is a refusal, so the row records it as denied.
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> PreferRows(
        IReadOnlyList<ChatMessage> messages,
        ToolCallRepository calls,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> reconciled = [];

        foreach (var message in messages)
        {
            List<AIContent> contents = [];

            foreach (var content in message.Contents)
            {
                contents.Add(content is FunctionResultContent result ? await PreferRow(result, calls, cancellationToken) : content);
            }

            var copy = message.Clone();
            copy.Contents = contents;
            reconciled.Add(copy);
        }

        return reconciled;
    }

    /// <summary>The outcome a call's row settled on, for a call history has no result for. Null while it waits for a human.</summary>
    public static ToolOutcome? SettledOutcome(ToolCall? row)
    {
        if (row?.Status is ToolCallStatus.PendingApproval or ToolCallStatus.Approved)
        {
            return null;
        }

        if (row is null || row.Status is ToolCallStatus.Running)
        {
            if (row is not null)
            {
                Complete(row, ToolCallStatus.Interrupted, Interrupted);
            }

            return Interrupted;
        }

        return row.Result is { } stored ? new ToolOutcome(stored, row.IsError) : Interrupted;
    }

    /// <summary>Denies every approval the thread is still waiting for, with the reason as the result.</summary>
    public static async Task ExpirePendingApprovals(
        ToolCallRepository calls,
        Guid threadId,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var row in await calls.ListPendingApprovals(threadId, cancellationToken))
        {
            Complete(row, ToolCallStatus.Denied, Error(reason));
        }
    }

    #endregion

    #region Private Methods

    private async Task<AIContent> PreferRow(FunctionResultContent result, ToolCallRepository calls, CancellationToken cancellationToken)
    {
        var row = await calls.GetToolCall(ToolCallIds.For(run.Execution.RunId, result.CallId), cancellationToken);

        if (row is null)
        {
            return result;
        }

        if (row.Status is ToolCallStatus.PendingApproval or ToolCallStatus.Approved)
        {
            Complete(row, ToolCallStatus.Denied, new ToolOutcome(MessageParts.ToJson(result.Result), true));
            return result;
        }

        var outcome = SettledOutcome(row)!;
        return new FunctionResultContent(result.CallId, outcome.Result);
    }

    private static ToolOutcome? Settle(ToolCall row) => row.Status switch
    {
        ToolCallStatus.PendingApproval or ToolCallStatus.Approved => null,
        _ when row.StartedAt is null && row.Status is ToolCallStatus.Running => null,
        _ => SettledOutcome(row),
    };

    private static ToolOutcome? Start(ToolCall row)
    {
        row.Status = ToolCallStatus.Running;
        row.StartedAt = DateTimeOffset.UtcNow;
        return null;
    }

    private static void Complete(ToolCall row, ToolCallStatus status, ToolOutcome outcome)
    {
        row.Status = status;
        row.Result = outcome.Result;
        row.IsError = outcome.IsError;
        row.StartedAt ??= DateTimeOffset.UtcNow;
        row.FinishedAt = DateTimeOffset.UtcNow;
    }

    #endregion
}
