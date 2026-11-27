namespace Drover.Server.Models;

public static class ModelServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the model providers this build ships, one <see cref="AddModelProvider{TProvider}"/> call each. The
    /// order of the calls is the order of the kinds in <c>GET /api/model-provider-kinds</c>, and so the order of the
    /// provider choice in every form: Venice, OpenAI, Anthropic and Gemini, the hosted providers that need only a key, then the compatible kind
    /// with the named providers it carries, so the choice starts on a provider that needs only a key and "Another
    /// compatible server" comes last. A new provider kind is one more call here.
    /// <c>Drover:Models:SignInOff</c> lists the sign-in kinds the operator switched off; the renewer keeps people's
    /// sign-ins from lapsing unused. A sign-in kind is also a provider kind: it registers here like any other.
    /// </summary>
    public static IServiceCollection AddDroverModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddModelProvider<VeniceProvider>(adapter => adapter.AddVeniceProvider(), VeniceProvider.HttpClientName);
        services.AddModelProvider<OpenAIProvider>(adapter => adapter.AddOpenAIProvider(), OpenAIProvider.HttpClientName);
        services.AddModelProvider<AnthropicProvider>(adapter => adapter.AddAnthropicProvider(), AnthropicProvider.HttpClientName);
        services.AddModelProvider<GeminiProvider>(adapter => adapter.AddGeminiProvider(), GeminiProvider.HttpClientName);
        services.AddModelProvider<OpenAICompatibleProvider>(
            adapter => adapter.AddOpenAICompatibleProvider(),
            OpenAICompatibleProvider.HttpClientName,
            OpenAICompatibleProvider.RealtimeHttpClientName);
        services.AddHostedService<ModelProviderStartupCheck>();
        services.AddScoped<ModelConnectionService>();
        services.Configure<ModelSignInOptions>(configuration.GetSection("Drover:Models"));
        services.AddHostedService<ModelSignInRenewer>();
        return services;
    }

    /// <summary>
    /// Registers one provider kind with the server. <paramref name="addAdapter"/> is the adapter's own registration,
    /// which must register <typeparamref name="TProvider"/> and add an <see cref="IModelClientProvider"/>; the call
    /// throws when it does not. The <see cref="SpaceOutboundHandler"/> is added to each client name in
    /// <paramref name="httpClients"/> and to no other, so the caller names every client the provider calls through.
    /// The handler tells the checked outbound handler that <c>AddDroverPolicy</c> installs for every client which
    /// space a call is for, so the private-address rule applies to the provider without the adapter knowing it. Two
    /// providers of one kind stop the server at start, in <see cref="ModelProviderCatalog"/>.
    /// </summary>
    public static IServiceCollection AddModelProvider<TProvider>(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> addAdapter,
        params string[] httpClients)
        where TProvider : class, IModelClientProvider
    {
        var providersBefore = services.Count(descriptor => descriptor.ServiceType == typeof(IModelClientProvider));
        addAdapter(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TProvider))
            || services.Count(descriptor => descriptor.ServiceType == typeof(IModelClientProvider)) == providersBefore)
        {
            throw new InvalidOperationException(
                $"The registration for {typeof(TProvider).Name} did not register {typeof(TProvider).Name} as a model provider.");
        }

        services.TryAddTransient<SpaceOutboundHandler>();

        foreach (var name in httpClients)
        {
            // It never follows a redirect: .NET drops Authorization on a redirect to another host but not a header the
            // adapter adds itself (Azure's api-key), so the key would go to whatever host the answer names.
            // It turns redirects off on the handler the policy built for every client, and replaces nothing.
            services.AddHttpClient(name)
                .ConfigurePrimaryHttpMessageHandler((handler, _) => OutboundHttp.DisableRedirects(handler))
                .AddHttpMessageHandler<SpaceOutboundHandler>();
        }

        return services;
    }

    #endregion
}
