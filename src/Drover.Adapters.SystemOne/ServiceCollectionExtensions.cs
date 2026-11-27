namespace Drover.Adapters.SystemOne;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers a decision model provider for every kind this adapter serves, and their one named client. The client
    /// has no timeout and no retry of its own: a rule check is answered once, inside the time the deployment policy
    /// gives it, and the caller cancels it there. The server adds the checked outbound handler to the client.
    /// </summary>
    public static IServiceCollection AddSystemOneDecisionModels(this IServiceCollection services)
    {
        services.AddHttpClient(SystemOneProvider.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        foreach (var kind in SystemOneKinds.All)
        {
            services.AddSingleton<IDecisionModelProvider>(provider => new SystemOneProvider(kind, provider.GetRequiredService<IHttpClientFactory>()));
        }

        return services;
    }

    #endregion
}
