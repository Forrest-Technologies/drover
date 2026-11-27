namespace Drover.Adapters.Anthropic;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers Anthropic with its own named client and pipeline. Replies are streamed and thinking models run for
    /// minutes, so the timeouts are wider than the resilience defaults; the vendor client's own retries and timeout are
    /// switched off, so this pipeline is the only one. A model call is repeated only when the provider certainly did not
    /// run it (<see cref="RetryRules"/>), after the wait it named.
    /// </summary>
    public static IServiceCollection AddAnthropicProvider(this IServiceCollection services)
    {
        services.AddHttpClient(AnthropicProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Context, arguments.Outcome));
                options.Retry.DelayGenerator = arguments => ValueTask.FromResult(RetryRules.WaitFor(arguments.Outcome));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

        services.AddSingleton<AnthropicProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<AnthropicProvider>());

        return services;
    }

    #endregion
}
