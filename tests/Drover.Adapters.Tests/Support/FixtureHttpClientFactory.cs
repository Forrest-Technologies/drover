namespace Drover.Adapters.Tests.Support;

/// <summary>Builds factory-managed clients with a recorded handler instead of a network transport.</summary>
public sealed class FixtureHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    #region Private Fields

    private readonly IHttpClientFactory clients = Build(handler);

    #endregion

    #region Interface Implementations

    public HttpClient CreateClient(string name) => clients.CreateClient(name);

    #endregion

    #region Private Methods

    private static IHttpClientFactory Build(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    #endregion
}
