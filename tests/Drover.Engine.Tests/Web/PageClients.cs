namespace Drover.Engine.Tests.Web;

/// <summary>The web page reader's client, made by the client factory as the server makes it, on a handler the test builds.</summary>
public static class PageClients
{
    #region Public Methods

    public static HttpClient Through(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(WebPageReader.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(WebPageReader.HttpClientName);
    }

    #endregion
}
