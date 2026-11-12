namespace Drover.Agents;

public static class AgentServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the agent runtime. <paramref name="resolveChatClient"/> gives each bot its model's chat client; the
    /// server wires it to the model providers. Needs <c>AddDroverData</c> and logging.
    /// </summary>
    public static IServiceCollection AddDroverAgents(
        this IServiceCollection services,
        ChatClientResolver resolveChatClient,
        CapabilityResolver? resolveCapabilities = null)
    {
        services.AddSingleton(resolveChatClient);
        services.AddSingleton(resolveCapabilities ?? (_ => []));
        services.AddSingleton<IAgentRuntime, AgentRuntime>();
        return services;
    }

    #endregion
}
