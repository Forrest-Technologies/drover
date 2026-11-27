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
        // Rules about replies go between the tool loop and the model, so every model call's answer passes them. A
        // tool's picture and the bot's notes are added below them, after the framework has handed the call's messages
        // to history, so neither is ever stored.
        var agent = new ReplyRules(new TransientImages(WithContext(chatClient, run), run), run.Governor)
            .AsAIAgent(options, loggerFactory);

        // Keep ChatClientAgent's own middleware ordering. In particular, its per-call history decorator must stay
        // inside the function loop for providers that manage conversation history on the server.
        if (agent.GetService<FunctionInvokingChatClient>() is { } invocation)
        {
            invocation.MaximumIterationsPerRequest = int.MaxValue;
        }

        return agent
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

    /// <summary>
    /// The chat client that gives every model call the project instructions and the notes, through the framework's
    /// context provider pipeline. Below the per-service-call history persistence, so each call in the tool loop has
    /// them and history never stores them. A conversation with neither is given the inner client unchanged.
    /// </summary>
    private static IChatClient WithContext(IChatClient chatClient, RunContext run)
    {
        if (run.Execution.Project is { Instructions.Length: > 0 } project)
        {
            chatClient = chatClient.AsBuilder().UseAIContextProviders(new ProjectInstructionSource(project)).Build();
        }

        if (run.Execution.ServiceMemory is { } serviceMemory)
        {
            chatClient = chatClient.AsBuilder().UseAIContextProviders(new ServiceRecall(serviceMemory)).Build();
        }

        if (run.Execution.Memory is { } memory)
        {
            chatClient = chatClient.AsBuilder().UseAIContextProviders(new NoteRecall(memory)).Build();
        }

        return chatClient;
    }

    private static AITool ToTool(ToolDefinition definition, RunContext run)
    {
        var function = new GovernedToolFunction(definition, run);
        return definition.RequiresApproval ? new ApprovalRequiredAIFunction(function) : function;
    }

    #endregion
}
