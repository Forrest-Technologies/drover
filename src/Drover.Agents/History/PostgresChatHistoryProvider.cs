namespace Drover.Agents.History;

/// <summary>
/// The thread's history, read from and written to Drover's <c>messages</c> inside a tenant transaction. With the
/// framework's per-service-call persistence on, it is read before and written after every model call.
/// </summary>
/// <remarks>
/// The framework writes a tool result together with the model call that follows it. A run killed after a tool ran
/// but before that call finished leaves a tool call in history with no result. Reading the history repairs that from
/// the <c>tool_calls</c> row the tool gate wrote: a finished call gets its stored result, and a call whose outcome is
/// unknown is marked interrupted, so nothing that may already have happened is repeated silently.
/// </remarks>
public sealed class PostgresChatHistoryProvider(RunContext run, ILogger logger) : ChatHistoryProvider
{
    #region Private Fields

    private const string InterruptedResult = "interrupted, outcome unknown";

    #endregion

    #region Protected Methods

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var supplied = context.RequestMessages
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);

        var messages = await run.Database.Transact(
            services => LoadAndRepair(services, supplied, cancellationToken),
            cancellationToken);

        return messages.Select(message => message.ToChatMessage());
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var response in context.ResponseMessages ?? [])
        {
            run.NoteRequestedCalls([.. response.Contents.OfType<FunctionCallContent>()]);
        }

        var execution = run.Execution;
        List<Message> messages =
        [
            .. context.RequestMessages.Concat(context.ResponseMessages ?? [])
                .Select(message => message.ToMessage(execution.RunId, execution.Bot.BotId, execution.PrincipalUserId))
                .OfType<Message>(),
        ];

        if (messages.Count == 0)
        {
            return;
        }

        await run.Database.Transact(
            services => services.GetRequiredService<MessageRepository>()
                .AppendMessages(run.Thread, messages, cancellationToken),
            cancellationToken);
    }

    #endregion

    #region History Repair

    private async Task<IReadOnlyList<Message>> LoadAndRepair(
        IServiceProvider services,
        HashSet<string> supplied,
        CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<MessageRepository>();
        var messages = await repository.ListMessages(run.Thread.Id, cancellationToken);
        var unanswered = FindUnansweredCalls(messages, supplied);

        if (unanswered.Count == 0)
        {
            return messages;
        }

        var toolCalls = services.GetRequiredService<ToolCallRepository>();
        var rows = (await toolCalls.ListToolCalls([.. unanswered.Select(call => call.ToolCallId)], cancellationToken))
            .ToDictionary(row => row.Id);
        List<MessagePart> results = [];

        foreach (var call in unanswered)
        {
            var row = rows.GetValueOrDefault(call.ToolCallId);

            if (row?.Status is ToolCallStatus.PendingApproval or ToolCallStatus.Approved)
            {
                // Waiting for a human. The framework answers it from the saved session when the run resumes.
                continue;
            }

            results.Add(await RepairedResult(call, row, toolCalls, cancellationToken));
        }

        if (results.Count == 0)
        {
            return messages;
        }

        var repair = new Message { Role = MessageRole.Tool, RunId = run.Execution.RunId, Parts = results };
        await repository.AppendMessages(run.Thread, [repair], cancellationToken);
        logger.LogInformation(
            "Repaired {Count} tool results in thread {ThreadId} for run {RunId}",
            results.Count,
            run.Thread.Id,
            run.Execution.RunId);
        return [.. messages, repair];
    }

    private static List<ToolCallPart> FindUnansweredCalls(IReadOnlyList<Message> messages, HashSet<string> supplied)
    {
        var answered = messages
            .SelectMany(message => message.Parts.OfType<ToolResultPart>())
            .Select(result => result.CallId)
            .Concat(supplied)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. messages
                .SelectMany(message => message.Parts.OfType<ToolCallPart>())
                .Where(call => !answered.Contains(call.CallId)),
        ];
    }

    private static async Task<MessagePart> RepairedResult(
        ToolCallPart call,
        ToolCall? row,
        ToolCallRepository toolCalls,
        CancellationToken cancellationToken)
    {
        if (row is { Status: ToolCallStatus.Completed or ToolCallStatus.Failed or ToolCallStatus.Denied, Result: { } result })
        {
            return new ToolResultPart(call.ToolCallId, call.CallId, row.IsError, result);
        }

        if (row is not null)
        {
            var tracked = await toolCalls.GetToolCall(row.Id, cancellationToken);
            tracked!.Status = ToolCallStatus.Interrupted;
            tracked.FinishedAt = DateTimeOffset.UtcNow;
        }

        return new ToolResultPart(call.ToolCallId, call.CallId, true, MessageParts.ToJson(InterruptedResult));
    }

    #endregion
}
