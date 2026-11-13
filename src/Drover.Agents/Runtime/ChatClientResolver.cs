namespace Drover.Agents.Runtime;

/// <summary>
/// Gives the chat client for one run. The server wires it to the model client the engine built for the run's bot.
/// </summary>
public delegate IChatClient ChatClientResolver(RunExecution execution);


/// <summary>
/// Gives the framework capabilities a bot has switched on, as context providers. Phase 0 bots have none; Phase 1
/// maps bot capabilities (todo list, modes, file memory) here.
/// </summary>
public delegate IEnumerable<AIContextProvider> CapabilityResolver(BotDefinition bot);
