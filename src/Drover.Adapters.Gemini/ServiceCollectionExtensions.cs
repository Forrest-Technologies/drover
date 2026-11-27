namespace Drover.Adapters.Gemini;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the Gemini provider with its own named client. Streaming replies and thinking models run for minutes,
    /// so the timeouts are wider than the resilience defaults. A model call is repeated only when the provider certainly
    /// did not run it: see <see cref="RetryRules"/>. The provider names its wait in the body of a 429, which a handler
    /// cannot read without consuming it, so a rate-limited call is not repeated here: it reaches the caller as "retry
    /// later" with the wait, and the caller decides.
    /// </summary>
    public static IServiceCollection AddGeminiProvider(this IServiceCollection services)
    {
        services.AddHttpClient(GeminiProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Context, arguments.Outcome) && !RetryRules.IsRateLimited(arguments.Outcome));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<GeminiProvider>());

        return services;
    }

    #endregion
}
