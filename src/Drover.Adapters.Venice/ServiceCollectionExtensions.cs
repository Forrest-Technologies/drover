namespace Drover.Adapters.Venice;

public static class ServiceCollectionExtensions
{
    #region Private Fields

    /// <summary>Past this wait, a rate-limited call is reported as "retry later" instead of held open.</summary>
    private static readonly TimeSpan maxBackoff = TimeSpan.FromSeconds(30);

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers the Venice provider. Streaming replies and reasoning models run for minutes, so the timeouts
    /// are wider than the resilience defaults. Retries wait for the provider's own rate-limit reset.
    /// Register the shared CompatibleChatClientBuilder first with AddOpenAICompatibleProvider in the composition root.
    /// </summary>
    public static IServiceCollection AddVeniceProvider(this IServiceCollection services)
    {
        services.AddHttpClient(VeniceProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(ShouldRetry(arguments.Context, arguments.Outcome));
                options.Retry.DelayGenerator = arguments => ValueTask.FromResult(WaitForReset(arguments.Outcome));

                // A rate limit says nothing about the service's health, so it must not pause calls for everyone.
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

        services.AddSingleton<IModelClientProvider, VeniceProvider>();

        return services;
    }

    #endregion

    #region Rate Limit Backoff

    private static bool ShouldRetry(ResilienceContext context, Outcome<HttpResponseMessage> outcome) =>
        RetryRules.ShouldRetry(context, outcome)
        && !(WaitForReset(outcome) is { } wait && wait > maxBackoff);

    private static TimeSpan? WaitForReset(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response
            ? VeniceHeaders.WaitUntilReset(
                name => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null,
                DateTimeOffset.UtcNow)
            : null;

    #endregion
}
