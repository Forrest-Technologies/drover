namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// What may be sent again. A model call is billed when the provider receives it, so a POST is repeated only when
/// the provider certainly did not run it: it said 429, or the connection never reached it. Reads repeat freely.
/// </summary>
internal static class RetryRules
{
    #region Public Methods

    public static bool ShouldRetry(ResilienceContext context, Outcome<HttpResponseMessage> outcome) =>
        !IsPolicyRefusal(outcome)
        && (IsRead(context)
            ? HttpClientResiliencePredicates.IsTransient(outcome)
            : IsRateLimited(outcome) || NeverReachedTheServer(outcome));

    /// <summary>
    /// A rate limit says nothing about the provider's health, and on a shared host one caller's limit must not
    /// pause everyone's calls.
    /// </summary>
    public static bool CountsAgainstCircuit(Outcome<HttpResponseMessage> outcome) =>
        HttpClientResiliencePredicates.IsTransient(outcome) && !IsRateLimited(outcome) && !IsPolicyRefusal(outcome);

    /// <summary>
    /// The deployment refused the address. Sending it again changes nothing, and on a shared host one space's
    /// refusal says nothing about the provider, so it must not pause calls for another space.
    /// </summary>
    public static bool IsPolicyRefusal(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is OutboundBlockedException;

    public static bool IsRateLimited(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests;

    #endregion

    #region Private Methods

    private static bool IsRead(ResilienceContext context) =>
        context.GetRequestMessage()?.Method.Method is "GET" or "HEAD" or "OPTIONS";

    private static bool NeverReachedTheServer(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException
        {
            HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError,
        };

    #endregion
}
