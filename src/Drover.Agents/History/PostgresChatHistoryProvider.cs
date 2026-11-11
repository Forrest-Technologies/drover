namespace Drover.Agents.History;

/// <summary>
/// The thread's history, read from and written to Drover's <c>messages</c> inside a tenant transaction. With the
/// framework's per-service-call persistence on, it is read before and written after every model call.
/// </summary>
/// <remarks>
/// <para>
/// The framework writes a tool result together with the model call that follows it. A run killed after a tool ran
/// but before that call finished leaves a tool call in history with no result. Reading the history repairs that from
/// the <c>tool_calls</c> row: a finished call gets its stored result, and a call whose outcome is unknown is marked
/// interrupted, so nothing that may already have happened is repeated silently.
/// </para>
/// <para>
/// A tool result on its way to the model or into history is replaced by its row's, where the row has one. A stale
/// session can make the framework reject an approval that was in fact given and acted on; the row says what really
/// happened. Calls pair with results by their <c>tool_calls</c> id, which is scoped to the run, so a provider that
/// reuses call ids across runs cannot make one run's result answer another run's call.
/// </para>
/// </remarks>
public sealed class PostgresChatHistoryProvider(RunContext run, ILogger logger) : ChatHistoryProvider
{
    #region Private Fields

    private readonly ToolCallLedger ledger = new(run);

    #endregion

    #region Protected Methods

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var (request, history) = await run.Database.Transact(
            async services =>
            {
                var reconciled = await ledger.PreferRows(
                    [.. context.RequestMessages],
                    services.GetRequiredService<ToolCallRepository>(),
                    cancellationToken);
                var messages = await LoadAndRepair(services, AnsweredIn(reconciled), cancellationToken);
                return (reconciled, messages);
            },
            cancellationToken);

        context.RequestMessages = request;
        return history.Select(message => message.ToChatMessage());
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
        await run.Database.Transact(
            async services =>
            {
                var reconciled = await ledger.PreferRows(
                    [.. context.RequestMessages.Concat(context.ResponseMessages ?? [])],
                    services.GetRequiredService<ToolCallRepository>(),
                    cancellationToken);
                List<Message> messages =
                [
                    .. reconciled
                        .Select(message => message.ToMessage(execution.RunId, execution.Bot.BotId, execution.PrincipalUserId))
                        .OfType<Message>(),
                ];

                if (messages.Count > 0)
                {
                    await services.GetRequiredService<MessageRepository>().AppendMessages(run.Thread, messages, cancellationToken);
                }
            },
            cancellationToken);
    }

    #endregion

    #region History Repair

    private HashSet<Guid> AnsweredIn(IReadOnlyList<ChatMessage> request) =>
    [
        .. request
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => ToolCallIds.For(run.Execution.RunId, result.CallId)),
    ];

    private async Task<IReadOnlyList<Message>> LoadAndRepair(
        IServiceProvider services,
        HashSet<Guid> supplied,
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
        List<MessagePart> results = [];

        foreach (var call in unanswered)
        {
            var row = await toolCalls.GetToolCall(call.ToolCallId, cancellationToken);

            // Null while the call waits for a human: the framework answers it from the saved session on resume.
            if (ToolCallLedger.SettledOutcome(row) is { } outcome)
            {
                results.Add(new ToolResultPart(call.ToolCallId, call.CallId, outcome.IsError, outcome.Result));
            }
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

    private static List<ToolCallPart> FindUnansweredCalls(IReadOnlyList<Message> messages, HashSet<Guid> supplied)
    {
        var answered = messages
            .SelectMany(message => message.Parts.OfType<ToolResultPart>())
            .Select(result => result.ToolCallId)
            .Concat(supplied)
            .ToHashSet();

        return
        [
            .. messages
                .SelectMany(message => message.Parts.OfType<ToolCallPart>())
                .Where(call => !answered.Contains(call.ToolCallId)),
        ];
    }

    #endregion
}
