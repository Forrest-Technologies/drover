namespace Drover.Agents.Runtime;

/// <summary>
/// Builds a framework agent that runs under Drover's governance: the run's granted tools, each call through the tool
/// gate, capabilities checked for name clashes, telemetry without message content. The runtime builds every bot
/// this way, and so does anything that puts a bot into a team.
/// </summary>
public static class GovernedAgentBuilder
{
    #region Public Methods

    public static AIAgent AsGovernedAgent(
        this IChatClient chatClient,
        RunContext run,
        ChatClientAgentOptions options,
        ILoggerFactory loggerFactory)
    {
        var tools = run.Execution.Tools.Select(definition => ToTool(definition, run)).ToList();
        RejectDuplicateNames(tools.Select(tool => tool.Name));

        options.Id ??= run.Execution.Bot.BotId.ToString("N");
        options.Name ??= run.Execution.Bot.Name;
        options.ChatOptions ??= new ChatOptions();
        options.ChatOptions.Instructions ??= run.Execution.Bot.Instructions;
        options.ChatOptions.ModelId ??= run.Execution.Bot.Model;
        options.ChatOptions.Tools = tools;
        List<string> catalogNames = [.. tools.Select(tool => tool.Name)];
        options.AIContextProviders = [.. (options.AIContextProviders ?? []).Select(provider => new CapabilityGuard(provider, catalogNames))];

        var gate = new ToolGate(run, loggerFactory.CreateLogger<ToolGate>());
        // Rules about replies go between the tool loop and the model, so every model call's answer passes them.
        return new ReplyRules(chatClient, run.Governor)
            .AsAIAgent(options, loggerFactory)
            .AsBuilder()
            .Use(gate.Invoke)
            .UseOpenTelemetry(AgentRuntime.TelemetrySource, telemetry => telemetry.EnableSensitiveData = false)
            .Build();
    }

    /// <summary>Throws when two tools in one run share a name.</summary>
    public static void RejectDuplicateNames(IEnumerable<string> names)
    {
        var duplicates = names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Two tools in this run are named {string.Join(", ", duplicates)}. Tool names must be unique, because the model calls tools by name.");
        }
    }

    #endregion

    #region Private Methods

    private static AITool ToTool(ToolDefinition definition, RunContext run)
    {
        var function = new GovernedToolFunction(definition, run);
        return definition.RequiresApproval ? new ApprovalRequiredAIFunction(function) : function;
    }

    #endregion
}
