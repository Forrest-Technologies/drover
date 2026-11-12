namespace Drover.Agents.Runtime;

/// <summary>Gives the chat client for a bot's model connection. The server wires it to the model providers.</summary>
public delegate IChatClient ChatClientResolver(BotDefinition bot);


/// <summary>
/// Gives the framework capabilities a bot has switched on, as context providers. Phase 0 bots have none; Phase 1
/// maps bot capabilities (todo list, modes, file memory) here.
/// </summary>
public delegate IEnumerable<AIContextProvider> CapabilityResolver(BotDefinition bot);
