namespace Drover.Adapters.E2B;

/// <summary>
/// What the control-plane client may send again. Every call it makes is safe to repeat: creation carries an
/// idempotency key, and the rest set a state rather than add one. E2B's <c>Should-Retry: false</c> is honored, and a
/// policy refusal is never repeated.
/// </summary>
internal static class RetryRules
{
    #region Public Methods

    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is not OutboundBlockedException
        && HttpClientResiliencePredicates.IsTransient(outcome)
        && !ProviderSaysNoRetry(outcome);

    /// <summary>A rate limit or a policy refusal says nothing about the provider's health.</summary>
    public static bool CountsAgainstCircuit(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is not OutboundBlockedException
        && HttpClientResiliencePredicates.IsTransient(outcome)
        && outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests;

    #endregion

    #region Private Methods

    private static bool ProviderSaysNoRetry(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.Headers.TryGetValues("Should-Retry", out var values) == true
        && string.Equals(values.FirstOrDefault(), "false", StringComparison.OrdinalIgnoreCase);

    #endregion
}
