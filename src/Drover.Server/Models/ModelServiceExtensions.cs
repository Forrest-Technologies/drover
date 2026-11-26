namespace Drover.Server.Models;

public static class ModelServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers both model providers, in the order their registrations require (the compatible transport first,
    /// because Venice builds its chat client from it). Every provider call goes through the checked outbound handler
    /// that <c>AddDroverPolicy</c> installs for every client, and the <see cref="SpaceOutboundHandler"/> states
    /// which space the call is for, so the private-address rule applies to model connections. <c>Drover:Models:SignInOff</c>
    /// lists the sign-in kinds the operator switched off; the renewer keeps people's sign-ins from lapsing unused.
    /// </summary>
    public static IServiceCollection AddDroverModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOpenAICompatibleProvider();
        services.AddVeniceProvider();
        services.TryAddTransient<SpaceOutboundHandler>();
        services.AddHttpClient(OpenAICompatibleProvider.HttpClientName).AddHttpMessageHandler<SpaceOutboundHandler>();
        services.AddHttpClient(OpenAICompatibleProvider.RealtimeHttpClientName).AddHttpMessageHandler<SpaceOutboundHandler>();
        services.AddHttpClient(VeniceProvider.HttpClientName).AddHttpMessageHandler<SpaceOutboundHandler>();
        services.AddScoped<ModelConnectionService>();
        services.Configure<ModelSignInOptions>(configuration.GetSection("Drover:Models"));
        services.AddHostedService<ModelSignInRenewer>();
        return services;
    }

    #endregion
}
