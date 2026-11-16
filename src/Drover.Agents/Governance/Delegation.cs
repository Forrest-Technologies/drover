namespace Drover.Agents.Governance;

/// <summary>
/// A call of the built-in <c>delegate</c> tool, which has passed the tool gate. The child bot is exposed as a function
/// through the framework's agent-as-tool, wrapped in a <see cref="GovernedAgent"/>, and invoked with the task; the
/// governed agent asks the governor to begin the child run, and the governor's result for the parent comes back as
/// the call's outcome.
/// </summary>
public static class Delegation
{
    #region Private Fields

    private const string BotIdArgument = "bot_id";

    private const string TaskArgument = "task";

    #endregion

    #region Public Methods

    public static async Task<ToolOutcome> Hand(RunContext run, ToolCallRequest call, CancellationToken cancellationToken)
    {
        if (Read(call.Arguments) is not var (botId, task))
        {
            return ToolCallLedger.Error($"Name the bot with {BotIdArgument}, as this tool lists it, and the task with {TaskArgument}.");
        }

        var request = new DelegationRequest(run.Execution.RunId, botId.ToString("N"))
        {
            ParentCallId = call.CallId,
            BotId = botId,
            Task = task,
        };
        var child = new GovernedAgent(new ChildRunAgent(run.Governor, request.AgentName), run.Governor, request);
        await child.AsAIFunction().InvokeAsync(new AIFunctionArguments { ["query"] = task }, cancellationToken);
        return child.Result ?? throw new InvalidOperationException($"The delegation of call {call.CallId} in run {call.RunId} gave no result.");
    }

    #endregion

    #region Private Methods

    private static (Guid BotId, string Task)? Read(JsonElement arguments) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty(BotIdArgument, out var id) && id.ValueKind is JsonValueKind.String && Guid.TryParse(id.GetString(), out var botId)
        && arguments.TryGetProperty(TaskArgument, out var text) && text.ValueKind is JsonValueKind.String && text.GetString() is { Length: > 0 } task
        && !string.IsNullOrWhiteSpace(task)
            ? (botId, task)
            : null;

    #endregion
}
