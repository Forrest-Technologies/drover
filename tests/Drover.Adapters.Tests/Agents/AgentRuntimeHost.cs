namespace Drover.Adapters.Tests.Agents;

/// <summary>One server process for the runtime: its own service provider over the shared test database.</summary>
public sealed class AgentRuntimeHost : IRuntimeHost
{
    #region Private Fields

    private readonly ServiceProvider provider;

    #endregion

    #region Constructors

    public AgentRuntimeHost(PostgresFixture postgres, ScriptedModel model, CapabilityResolver? capabilities = null)
    {
        var services = postgres.NewServices();
        services.AddDroverAgents(_ => new ScriptedChatClient(model), capabilities);
        provider = services.BuildServiceProvider();
    }

    #endregion

    #region Properties

    public IAgentRuntime Runtime => provider.GetRequiredService<IAgentRuntime>();

    public IServiceProvider Services => provider;

    #endregion

    #region Interface Implementations

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    #endregion
}
