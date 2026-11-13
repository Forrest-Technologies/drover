namespace Drover.Adapters.Tests.Mcp;

/// <summary>The tool source conformance suite, run against the adapter for remote tool servers.</summary>
public sealed class McpToolSourceConformanceTests : ToolSourceConformance
{
    #region Overrides

    protected override ToolSourceUnderTest CreateSource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMcpToolSource();
        var provider = services.BuildServiceProvider();
        return new ToolSourceUnderTest(provider.GetRequiredService<IToolSource>(), provider);
    }

    protected override async Task<IToolServer> StartServer(ToolServerSpec spec) => await SdkToolServer.Start(spec);

    #endregion
}
