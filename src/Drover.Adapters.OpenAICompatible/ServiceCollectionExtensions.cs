namespace Drover.Adapters.OpenAICompatible;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the compatible-endpoint provider. Streaming replies and reasoning models run for minutes,
    /// so the timeouts are wider than the resilience defaults, which are sized for ordinary API calls.
    /// </summary>
    public static IServiceCollection AddOpenAICompatibleProvider(this IServiceCollection services)
    {
        services.AddHttpClient(OpenAICompatibleProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
            });

        services.AddSingleton<IModelClientProvider, OpenAICompatibleProvider>();

        return services;
    }
}
