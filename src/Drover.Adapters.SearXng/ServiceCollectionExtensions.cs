namespace Drover.Adapters.SearXng;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the SearXNG search adapter and its client. The client keeps the outbound handler every client gets,
    /// and no retry: a search the model asked for is answered once or refused, inside the time the policy allows.
    /// </summary>
    public static IHttpClientBuilder AddSearXngSearch(this IServiceCollection services, Action<SearXngOptions> configure)
    {
        services.AddOptions<SearXngOptions>().Configure(configure);
        services.AddSingleton<IWebSearch, SearXngSearch>();
        return services.AddHttpClient(SearXngSearch.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
    }

    #endregion
}
