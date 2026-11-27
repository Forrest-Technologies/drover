namespace Drover.Adapters.Anthropic;

/// <summary>
/// What may be sent again. A model call is billed when the provider runs it, so a POST is repeated only when the
/// provider certainly did not: it said it is overloaded (529), it said 429 and named a short wait, or the connection
/// never reached it. A 429 with no <c>retry-after</c> is a spend cap that keeps failing until access resumes, so it is
/// not repeated. Reads repeat freely. Source (read 2026-11-27): https://platform.claude.com/docs/en/api/errors.
/// </summary>
internal static class RetryRules
{
    #region Private Fields

    /// <summary>The status the provider answers when it is overloaded.</summary>
    public const int Overloaded = 529;

    /// <summary>Past this wait, a rate-limited call is reported as "retry later" instead of held open.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    #endregion

    #region Public Methods

    public static bool ShouldRetry(ResilienceContext context, Outcome<HttpResponseMessage> outcome) =>
        !IsPolicyRefusal(outcome)
        && (IsRead(context)
            ? HttpClientResiliencePredicates.IsTransient(outcome) || IsOverloaded(outcome)
            : IsOverloaded(outcome) || IsShortRateLimit(outcome) || NeverReachedTheServer(outcome));

    /// <summary>
    /// A rate limit or an overloaded provider says nothing about one connection's health, and on a shared host one
    /// caller's limit must not pause everyone's calls.
    /// </summary>
    public static bool CountsAgainstCircuit(Outcome<HttpResponseMessage> outcome) =>
        HttpClientResiliencePredicates.IsTransient(outcome)
        && outcome.Result?.StatusCode is not HttpStatusCode.TooManyRequests
        && !IsOverloaded(outcome)
        && !IsPolicyRefusal(outcome);

    /// <summary>The wait the provider named, for the retry's delay.</summary>
    public static TimeSpan? WaitFor(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.Headers.TryGetValues("retry-after", out var values) == true
            ? AnthropicFailures.ParseRetryAfter(values.FirstOrDefault(), DateTimeOffset.UtcNow)
            : null;

    #endregion

    #region Private Methods

    private static bool IsPolicyRefusal(Outcome<HttpResponseMessage> outcome) => outcome.Exception is OutboundBlockedException;

    private static bool IsOverloaded(Outcome<HttpResponseMessage> outcome) => (int?)outcome.Result?.StatusCode == Overloaded;

    private static bool IsShortRateLimit(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests && WaitFor(outcome) is { } wait && wait <= MaxWait;

    private static bool IsRead(ResilienceContext context) =>
        context.GetRequestMessage()?.Method.Method is "GET" or "HEAD" or "OPTIONS";

    private static bool NeverReachedTheServer(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException
        {
            HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError,
        };

    #endregion
}
