namespace Drover.Adapters.Anthropic;

/// <summary>
/// Keeps the rate-limit headers of the last answer of one call: <c>retry-after</c> and
/// <c>anthropic-ratelimit-{requests|tokens|input-tokens|output-tokens}-{limit|remaining|reset}</c>
/// (https://platform.claude.com/docs/en/api/rate-limits, read 2026-11-27). One capture per call, never shared.
/// </summary>
internal sealed class AnthropicHeaderCapture : DelegatingHandler
{
    #region Properties

    public IReadOnlyDictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long the provider asked to wait, when it said.</summary>
    public TimeSpan? RetryAfter =>
        Headers.TryGetValue("retry-after", out var value) ? AnthropicFailures.ParseRetryAfter(value, DateTimeOffset.UtcNow) : null;

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Headers = response.Headers
            .Where(header => header.Key.StartsWith("anthropic-ratelimit-", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("retry-after", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
        return response;
    }

    #endregion
}
