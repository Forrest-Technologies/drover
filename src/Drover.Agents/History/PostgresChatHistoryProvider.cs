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
/// <para>
/// A message its person sent to steer the run joins here, at a model call: it is handed to the model after the
/// request's own messages, so after any tool results, and stored in that place when the call returns. Only messages
/// of the person the run acts for are taken. A message for after the run is left alone.
/// </para>
/// <para>
/// The model is always given a tool call followed by its results, whatever order they were stored in: a run stopped
/// in the middle of a call leaves it unanswered, and its result is stored later, after whatever was said meanwhile.
/// </para>
/// </remarks>
public sealed class PostgresChatHistoryProvider(RunContext run, ILogger logger) : ChatHistoryProvider
{
    #region Private Fields

    private readonly ToolCallLedger ledger = new(run);

    // The ids of the steering messages handed to the model call in flight, as the framework's message ids.
    private HashSet<string> steering = [];

    #endregion

    #region Protected Methods

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var (request, history, cutOff, steers) = await run.Database.Transact(
            async services =>
            {
                var reconciled = await ledger.PreferRows(
                    [.. context.RequestMessages],
                    services.GetRequiredService<ToolCallRepository>(),
                    cancellationToken);
                var (messages, rerun) = await LoadAndRepair(services, AnsweredIn(reconciled), cancellationToken);
                var waiting = await ListSteering(services.GetRequiredService<MessageRepository>(), cancellationToken);
                return (reconciled, messages, rerun, waiting);
            },
            cancellationToken);

        List<ChatMessage> steered = [.. steers.Select(message => message.ToChatMessage())];
        steering = [.. steered.Select(message => message.MessageId!)];
        context.RequestMessages = [.. request, .. steered];

        // The tools run here, after the transaction above has closed: no transaction is ever held across a tool call.
        var complete = cutOff.Count == 0 ? history : [.. history, await RunAgain(cutOff, cancellationToken)];
        return MessageOrder.ResultsAfterCalls(complete).Select(message => message.ToChatMessage());
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
        var taken = steering;
        steering = [];

        // The steering messages are rows already; they are given their place below, not stored a second time.
        List<ChatMessage> request = [.. context.RequestMessages.Where(message => message.MessageId is null || !taken.Contains(message.MessageId))];
        await run.Database.Transact(
            async services =>
            {
                var repository = services.GetRequiredService<MessageRepository>();
                var reconciled = await ledger.PreferRows(
                    [.. request, .. context.ResponseMessages ?? []],
                    services.GetRequiredService<ToolCallRepository>(),
                    cancellationToken);
                List<(bool IsReply, Message Message)> converted =
                [
                    .. reconciled
                        .Select((message, index) => (
                            IsReply: index >= request.Count,
                            Message: message.ToMessage(execution.RunId, execution.Bot.BotId, execution.PrincipalUserId, keepReplyId: index >= request.Count)))
                        .Where(pair => pair.Message is not null)
                        .Select(pair => (pair.IsReply, WithRedactedArguments(pair.Message!))),
                ];
                List<Message> steers = [.. (await ListSteering(repository, cancellationToken)).Where(message => taken.Contains(message.Id.ToString("N")))];

                foreach (var message in steers)
                {
                    message.RunId = execution.RunId;
                }

                List<Message> messages =
                [
                    .. converted.Where(pair => !pair.IsReply).Select(pair => pair.Message),
                    .. steers,
                    .. converted.Where(pair => pair.IsReply).Select(pair => pair.Message),
                ];

                if (messages.Count > 0)
                {
                    await repository.AppendMessages(run.Thread, messages, cancellationToken);
                }
            },
            cancellationToken);
    }

    #endregion

    #region Steering

    /// <summary>
    /// The pending messages that steer this run: sent to steer, by the person the run acts for. A run takes
    /// instructions from nobody else, so another member's message in a shared thread waits for a run of its own.
    /// </summary>
    private async Task<IReadOnlyList<Message>> ListSteering(MessageRepository repository, CancellationToken cancellationToken) =>
    [
        .. (await repository.ListPendingMessages(run.Thread.Id, cancellationToken))
            .Where(message => message.Steers && message.AuthorUserId == run.Execution.PrincipalUserId),
    ];

    #endregion

    #region Redaction

    /// <summary>
    /// Tool arguments are stored after redaction wherever they are stored, the thread's messages included. Results
    /// are already redacted when the governor returns them.
    /// </summary>
    private Message WithRedactedArguments(Message message)
    {
        message.Parts =
        [
            .. message.Parts.Select(part => part is ToolCallPart call
                ? call with { Arguments = run.Governor.Redact(call.Arguments) }
                : part),
        ];
        return message;
    }

    #endregion

    #region History Repair

    private HashSet<Guid> AnsweredIn(IReadOnlyList<ChatMessage> request) =>
    [
        .. request
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => ToolCallIds.For(run.Execution.RunId, result.CallId)),
    ];

    /// <summary>
    /// The thread's messages with missing tool results added from their rows, and the calls that must run again: the
    /// ones this run started, a kill cut off, and whose tool only reads.
    /// </summary>
    private async Task<(IReadOnlyList<Message> Messages, IReadOnlyList<CutOffCall> RunAgain)> LoadAndRepair(
        IServiceProvider services,
        HashSet<Guid> supplied,
        CancellationToken cancellationToken)
    {
        var repository = services.GetRequiredService<MessageRepository>();
        var messages = await repository.ListMessages(run.Thread.Id, cancellationToken);
        var unanswered = FindUnansweredCalls(messages, supplied);

        if (unanswered.Count == 0)
        {
            return (messages, []);
        }

        var toolCalls = services.GetRequiredService<ToolCallRepository>();
        List<MessagePart> results = [];
        List<CutOffCall> cutOff = [];

        foreach (var call in unanswered)
        {
            var row = await toolCalls.GetToolCall(call.ToolCallId, cancellationToken);

            if (FindToolToRunAgain(row) is { } tool)
            {
                cutOff.Add(new CutOffCall(call, tool));
                continue;
            }

            // Null while the call waits for a human: the framework answers it from the saved session on resume.
            if (ToolCallLedger.SettledOutcome(row) is { } outcome)
            {
                results.Add(new ToolResultPart(call.ToolCallId, call.CallId, outcome.IsError, outcome.Result));
            }
        }

        if (results.Count == 0)
        {
            return (messages, cutOff);
        }

        var repair = new Message { Role = MessageRole.Tool, RunId = run.Execution.RunId, Parts = results };
        await repository.AppendMessages(run.Thread, [repair], cancellationToken);
        logger.LogInformation(
            "Repaired {Count} tool results in thread {ThreadId} for run {RunId}",
            results.Count,
            run.Thread.Id,
            run.Execution.RunId);
        return ([.. messages, repair], cutOff);
    }

    /// <summary>
    /// The tool of a call that may run again: this run started it, it never finished, and its definition says it only
    /// reads. Anything else might already have happened, so it is never repeated.
    /// </summary>
    private ToolDefinition? FindToolToRunAgain(ToolCall? row) =>
        row is { Status: ToolCallStatus.Running, StartedAt: not null } && row.RunId == run.Execution.RunId
            ? run.Execution.Tools.FirstOrDefault(tool => tool.ToolId == row.ToolId && tool.MayRunAgainAfterInterruption)
            : null;

    /// <summary>Runs the cut-off read-only calls again, through the governor, and stores their results as a tool message.</summary>
    private async Task<Message> RunAgain(IReadOnlyList<CutOffCall> cutOff, CancellationToken cancellationToken)
    {
        List<MessagePart> results = [];

        foreach (var (call, tool) in cutOff)
        {
            var request = new ToolCallRequest
            {
                RunId = run.Execution.RunId,
                CallId = call.CallId,
                ToolName = tool.Name,
                ToolId = tool.ToolId,
                Arguments = call.Arguments,
            };
            var (status, outcome) = await RunAgain(request, cancellationToken);
            await ledger.Finish(request, tool.ToolId, status, outcome, cancellationToken);
            results.Add(new ToolResultPart(call.ToolCallId, call.CallId, outcome.IsError, outcome.Result));
            logger.LogInformation("Tool call {CallId} of run {RunId} was cut off and ran again: {Status}", call.CallId, request.RunId, status);
        }

        var repair = new Message { Role = MessageRole.Tool, RunId = run.Execution.RunId, Parts = results };
        await run.Database.Transact(
            services => services.GetRequiredService<MessageRepository>().AppendMessages(run.Thread, [repair], cancellationToken),
            cancellationToken);
        return repair;
    }

    /// <summary>The grant is checked again, as for any call. A call that may no longer run is settled as interrupted.</summary>
    private async Task<(ToolCallStatus Status, ToolOutcome Outcome)> RunAgain(ToolCallRequest request, CancellationToken cancellationToken)
    {
        var decision = await run.Governor.Decide(request, cancellationToken);

        if (decision.Verdict is not ToolGateVerdict.Run)
        {
            return (ToolCallStatus.Interrupted, ToolCallLedger.Interrupted);
        }

        try
        {
            var outcome = await run.Governor.InvokeTool(request, cancellationToken);
            return (outcome.IsError ? ToolCallStatus.Failed : ToolCallStatus.Completed, outcome);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Tool {ToolName} failed when run again in run {RunId}", request.ToolName, request.RunId);
            return (ToolCallStatus.Failed, ToolCallLedger.Error("The tool failed."));
        }
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

    #region Nested Types

    private sealed record CutOffCall(ToolCallPart Call, ToolDefinition Tool);

    #endregion
}
