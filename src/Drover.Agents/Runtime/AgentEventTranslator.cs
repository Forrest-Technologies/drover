namespace Drover.Agents.Runtime;

/// <summary>Turns the framework's streaming updates into Drover's events, and records what a run waits for.</summary>
public sealed class AgentEventTranslator(RunContext run)
{
    #region Private Fields

    private readonly List<(string RequestId, FunctionCallContent Call)> held = [];

    private readonly List<(string RequestId, FunctionCallContent Call)> cleared = [];

    #endregion

    #region Properties

    /// <summary>True once a held call waits for a person.</summary>
    public bool Waiting { get; private set; }

    #endregion

    #region Public Methods

    public async Task<IReadOnlyList<AgentEvent>> Translate(AgentResponseUpdate update, CancellationToken cancellationToken)
    {
        List<AgentEvent> events = [];

        foreach (var content in update.Contents)
        {
            var translated = await Translate(content, cancellationToken);

            if (translated is not null)
            {
                events.Add(translated);
            }
        }

        return events;
    }

    /// <summary>
    /// Decides, for each call the framework held for approval since the last time, whether a person is asked. A call
    /// the governor clears is recorded as cleared on its row and goes on as a plain call, with no approval event; any
    /// other waits for a person. Asked once the held calls are recorded and the session saved.
    /// </summary>
    public async Task<IReadOnlyList<AgentEvent>> SettleHeldCalls(CancellationToken cancellationToken)
    {
        List<AgentEvent> events = [];
        var ledger = new ToolCallLedger(run);

        foreach (var (requestId, call) in held)
        {
            var arguments = MessageParts.ToJson(call.Arguments);

            if (await run.Governor.ClearsWithoutAPerson(call.CallId, call.Name, arguments, cancellationToken)
                && await ledger.Clear(call.CallId, arguments, cancellationToken))
            {
                cleared.Add((requestId, call));
                events.Add(new ToolCallRequested(call.CallId, call.Name, arguments));
                continue;
            }

            Waiting = true;
            events.Add(new ApprovalRequested(requestId, call.CallId, call.Name, arguments));
        }

        held.Clear();
        return events;
    }

    /// <summary>
    /// The framework's answers for the calls cleared since the last time, taken once. Answered only when no call of
    /// the same stop waits for a person: the framework rejects a held call left out of an answer, so a cleared call
    /// that shares a stop with one a person is asked about is answered with the person's answer instead.
    /// </summary>
    public List<ChatMessage> TakeClearedAnswers()
    {
        if (Waiting || cleared.Count == 0)
        {
            return [];
        }

        List<AIContent> answers = [.. cleared.Select(clear => new ToolApprovalResponseContent(clear.RequestId, true, new FunctionCallContent(clear.Call.CallId, string.Empty)))];
        cleared.Clear();
        return [new ChatMessage(ChatRole.User, answers)];
    }

    #endregion

    #region Private Methods

    private async Task<AgentEvent?> Translate(AIContent content, CancellationToken cancellationToken)
    {
        switch (content)
        {
            case TextContent { Text.Length: > 0 } text:
                return new TextDelta(text.Text);
            case TextReasoningContent { Text.Length: > 0 } reasoning:
                return new ReasoningDelta(reasoning.Text);
            case FunctionCallContent call:
                return new ToolCallRequested(call.CallId, call.Name, MessageParts.ToJson(call.Arguments));
            case FunctionResultContent result:
                return new ToolCallFinished(result.CallId, result.Exception is not null || run.WasError(result.CallId), MessageParts.ToJson(result.Result));
            case ToolApprovalRequestContent { ToolCall: FunctionCallContent call } request:
                // Whether a person is asked is decided once the stop is saved: see SettleHeldCalls.
                await RecordPendingApproval(request.RequestId, call, cancellationToken);
                held.Add((request.RequestId, call));
                return null;
            case UsageContent usage:
                await run.Governor.RecordUsage(
                    new ModelUsage(usage.Details.InputTokenCount ?? 0, usage.Details.OutputTokenCount ?? 0),
                    cancellationToken);
                return null;
            default:
                return null;
        }
    }

    private Task RecordPendingApproval(string requestId, FunctionCallContent call, CancellationToken cancellationToken) =>
        run.FindTool(call.Name) is { } tool
            ? new ToolCallLedger(run).RecordPendingApproval(requestId, call, tool, cancellationToken)
            : Task.CompletedTask;

    #endregion
}
