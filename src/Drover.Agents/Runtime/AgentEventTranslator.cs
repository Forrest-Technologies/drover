namespace Drover.Agents.Runtime;

/// <summary>Turns the framework's streaming updates into Drover's events, and records what a run waits for.</summary>
public sealed class AgentEventTranslator(RunContext run)
{
    #region Properties

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
                Waiting = true;
                await RecordPendingApproval(call, cancellationToken);
                return new ApprovalRequested(request.RequestId, call.CallId, call.Name, MessageParts.ToJson(call.Arguments));
            case UsageContent usage:
                await run.Governor.RecordUsage(
                    new ModelUsage(usage.Details.InputTokenCount ?? 0, usage.Details.OutputTokenCount ?? 0),
                    cancellationToken);
                return null;
            default:
                return null;
        }
    }

    private Task RecordPendingApproval(FunctionCallContent call, CancellationToken cancellationToken)
    {
        if (run.FindTool(call.Name) is not { } tool)
        {
            return Task.CompletedTask;
        }

        return run.Database.Transact(
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
    }

    #endregion
}
