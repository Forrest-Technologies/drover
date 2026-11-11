namespace Drover.Agents.Governance;

/// <summary>
/// A granted tool as the framework sees it. Calling it hands the call to the engine through
/// <see cref="IRunGovernor.InvokeTool"/>; the tool gate has already decided by then that it may run.
/// </summary>
public sealed class GovernedToolFunction(ToolDefinition definition, RunContext run) : AIFunction
{
    #region Properties

    public override string Name => definition.Name;

    public override string Description => definition.Description;

    public override JsonElement JsonSchema => definition.InputSchema;

    public ToolDefinition Definition => definition;

    #endregion

    #region Protected Methods

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId
            ?? throw new InvalidOperationException("A governed tool runs only inside the framework's function loop.");

        var request = new ToolCallRequest
        {
            RunId = run.Execution.RunId,
            CallId = callId,
            ToolName = definition.Name,
            ToolId = definition.ToolId,
            Arguments = MessageParts.ToJson(arguments),
        };

        // The outcome travels back through the gate, which records whether it was an error.
        return await run.Governor.InvokeTool(request, cancellationToken);
    }

    #endregion
}
