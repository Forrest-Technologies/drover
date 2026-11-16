namespace Drover.Server.Push;

public static class PushServiceExtensions
{
    #region Private Fields

    private const string HttpClientName = "Drover.WebPush";

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers Web Push when the server has a public https address to sign with and its key pair can be read, and
    /// says why not otherwise. Pushes go to addresses a device handed in, so they use the checked outbound client,
    /// which refuses private addresses, and never follow a redirect.
    /// </summary>
    public static IServiceCollection AddDroverPush(this IServiceCollection services, IConfiguration configuration, string keyRingPath)
    {
        var subject = PushAvailability.SubjectFor(PublicAddress.Read(configuration));

        if (subject is null)
        {
            services.AddSingleton(PushAvailability.Off(PushAvailability.NoAddress));
            return services;
        }

        services.AddSingleton(provider => new PushKeyStore(
            keyRingPath,
            provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<ILogger<PushKeyStore>>()).Open(subject));
        services.AddHttpClient(HttpClientName, client => { client.Timeout = TimeSpan.FromSeconds(30); })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                OutboundHttp.CreateHandlerWithoutRedirects(provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules));

        // The notifier takes every channel there is. With keys it cannot read there is none, and push stays off.
        services.AddSingleton<IEnumerable<IPushChannel>>(provider =>
            provider.GetRequiredService<PushAvailability>().Keys is { } keys
                ? [new WebPushChannel(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                    keys,
                    provider.GetRequiredService<ILogger<WebPushChannel>>())]
                : []);
        return services;
    }

    #endregion
}
