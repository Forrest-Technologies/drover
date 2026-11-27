namespace Drover.Server.Web;

public static class WebServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the web tools. <c>web.fetch</c> is on every server: its client is the checked outbound handler without
    /// cookies and with redirects left to the reader, and no request of it states a space's rule, so a page is never
    /// read from a private address whatever the deployment allows. <c>web.search</c> is registered only when the
    /// operator names a search service in <c>Drover:WebSearch:SearXng:BaseUrl</c>, an http or https address; without
    /// one the server has no search, which is not an error. That address is the operator's own configuration, which no
    /// space, person or bot can change, so it may be private (a search service beside the server is the usual setup)
    /// even where the deployment forbids private addresses, and its client disables redirects and cookies so the
    /// operator permission never follows a redirect to another origin.
    /// </summary>
    public static IServiceCollection AddDroverWebTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(WebPageReader.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider =>
                OutboundHttp.CreateHandlerWithoutRedirects(provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules));
        services.AddWebFetch(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient(WebPageReader.HttpClientName));

        if (ReadSearchAddress(configuration) is not { } address)
        {
            return services;
        }

        services.TryAddTransient<OperatorAddressHandler>();
        services.AddSearXngSearch(options => options.BaseUrl = address)
            .ConfigurePrimaryHttpMessageHandler(OutboundHttp.CreateOperatorHandler)
            .AddHttpMessageHandler<OperatorAddressHandler>();
        return services.AddWebSearch();
    }

    #endregion

    #region Private Methods

    private static Uri? ReadSearchAddress(IConfiguration configuration) =>
        configuration[SearchAddressSetting] switch
        {
            null or "" => null,
            var text when Uri.TryCreate(text.Trim(), UriKind.Absolute, out var address) && address.Scheme is "http" or "https" => address,
            _ => throw new InvalidOperationException(
                $"{SearchAddressSetting} must be the http or https address of a SearXNG instance. Correct it, or remove it to run without web search."),
        };

    #endregion

    #region Constants

    public const string SearchAddressSetting = "Drover:WebSearch:SearXng:BaseUrl";

    #endregion
}
