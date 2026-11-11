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
    /// </summary>
    public static IServiceCollection AddVeniceProvider(this IServiceCollection services)
    {
        services.AddHttpClient(VeniceProvider.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments => ValueTask.FromResult(ShouldRetry(arguments.Outcome));
                options.Retry.DelayGenerator = arguments => ValueTask.FromResult(WaitForReset(arguments.Outcome));

                // Every tenant shares this one host. One key being rate limited must not open the circuit for all.
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome)
                        && arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);
            });

        services.AddSingleton<IModelClientProvider, VeniceProvider>();

        return services;
    }

    #endregion

    #region Rate Limit Backoff

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome) =>
        HttpClientResiliencePredicates.IsTransient(outcome)
        && !(WaitForReset(outcome) is { } wait && wait > maxBackoff);

    private static TimeSpan? WaitForReset(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response
        && response.Headers.TryGetValues(VeniceHeaders.RateLimitReset, out var values)
            ? VeniceHeaders.WaitUntilReset(values.FirstOrDefault(), DateTimeOffset.UtcNow)
            : null;

    #endregion
}
