namespace Drover.Adapters.Tests.Support;

/// <summary>Hands every adapter an HttpClient that talks to a recorded fixture.</summary>
public sealed class FixtureHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
