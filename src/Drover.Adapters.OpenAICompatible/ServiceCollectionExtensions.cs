namespace Drover.Adapters.OpenAICompatible;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the compatible-endpoint provider. Streaming replies and reasoning models run for minutes, so the
    /// timeouts are wider than the resilience defaults. Each endpoint gets its own pipeline, so one failing server
    /// cannot pause calls to another. Model calls are not repeated after a timeout or a server error, because the
    /// provider may already have run, and billed, them: see <see cref="RetryRules"/>.
    /// </summary>
    public static IServiceCollection AddOpenAICompatibleProvider(this IServiceCollection services)
    {
        services.AddHttpClient(OpenAICompatibleProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Context, arguments.Outcome));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

        services.AddSingleton<IModelClientProvider, OpenAICompatibleProvider>();

        return services;
    }
}
