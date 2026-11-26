namespace Drover.Server.Models;

public static class ModelServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the model providers this build ships, one <see cref="AddModelProvider{TProvider}"/> call each, in the
    /// order their registrations require (the compatible transport first, because Venice builds its chat client from
    /// it). A new provider kind is one more call here.
    /// </summary>
    public static IServiceCollection AddDroverModels(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddModelProvider<OpenAICompatibleProvider>(
            adapter => adapter.AddOpenAICompatibleProvider(),
            OpenAICompatibleProvider.HttpClientName,
            OpenAICompatibleProvider.RealtimeHttpClientName);
        services.AddModelProvider<VeniceProvider>(adapter => adapter.AddVeniceProvider(), VeniceProvider.HttpClientName);
        services.AddHostedService<ModelProviderStartupCheck>();
        services.AddScoped<ModelConnectionService>();
        return services;
    }

    /// <summary>
    /// Registers one provider kind with the server. <paramref name="addAdapter"/> is the adapter's own registration,
    /// which must make <typeparamref name="TProvider"/> an <see cref="IModelClientProvider"/>. Every named HTTP client
    /// the provider calls through then gets the <see cref="SpaceOutboundHandler"/>, so the checked outbound handler
    /// that <c>AddDroverPolicy</c> installs for every client knows which space a call is for, and the private-address
    /// rule applies to the provider without the adapter knowing it. Two providers of one kind stop the server at
    /// start, in <see cref="ModelProviderCatalog"/>.
    /// </summary>
    public static IServiceCollection AddModelProvider<TProvider>(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> addAdapter,
        params string[] httpClients)
        where TProvider : class, IModelClientProvider
    {
        addAdapter(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TProvider)))
        {
            throw new InvalidOperationException(
                $"The registration for {typeof(TProvider).Name} did not register {typeof(TProvider).Name}.");
        }

        services.TryAddTransient<SpaceOutboundHandler>();

        foreach (var name in httpClients)
        {
            services.AddHttpClient(name).AddHttpMessageHandler<SpaceOutboundHandler>();
        }

        return services;
    }

    #endregion
}
