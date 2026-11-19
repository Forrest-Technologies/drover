namespace Drover.Adapters.SearXng;

/// <summary>
/// Searches through the operator's SearXNG instance: <c>GET /search?q=...&amp;format=json</c>, read as its JSON
/// answer's <c>results</c>, each a title, an address, and an excerpt. Results without an http or https address are
/// left out. Safe search is asked for at its moderate level. The answer is read up to the request's byte limit and
/// refused past it.
/// </summary>
public sealed class SearXngSearch(IHttpClientFactory clients, IOptions<SearXngOptions> options, ILogger<SearXngSearch> logger) : IWebSearch
{
    #region Properties

    public string Kind => "searxng";

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<WebSearchResult>> Search(WebSearchRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, SearchAddress(request.Query));
        message.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await clients.CreateClient(HttpClientName)
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureAnswered(response);
            using var answer = await ReadAnswer(response.Content, request.MaxResponseBytes, cancellationToken);
            return [.. ReadResults(answer.RootElement).Take(request.MaxResults)];
        }
        catch (OutboundBlockedException blocked)
        {
            throw new WebSearchException(WebSearchFailure.Blocked, blocked.Message, blocked);
        }
        catch (HttpRequestException failure)
        {
            logger.LogWarning(failure, "The search service could not be reached");
            throw new WebSearchException(WebSearchFailure.Unavailable, "The search service could not be reached.", failure);
        }
        catch (JsonException malformed)
        {
            throw new WebSearchException(WebSearchFailure.Malformed, "The search service's answer could not be read.", malformed);
        }
    }

    #endregion

    #region Private Methods

    private Uri SearchAddress(string query)
    {
        var root = options.Value.BaseUrl.AbsoluteUri.TrimEnd('/');
        return new Uri($"{root}/search?q={Uri.EscapeDataString(query)}&format=json&safesearch=1&pageno=1");
    }

    private static void EnsureAnswered(HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return;
            case HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized:
                // SearXNG answers 403 when its JSON output is not turned on in its settings.
                throw new WebSearchException(
                    WebSearchFailure.Refused,
                    "The search service refused the request. Its JSON output may be turned off in its settings (search.formats).");
            case HttpStatusCode.TooManyRequests:
                throw new WebSearchException(WebSearchFailure.Unavailable, "The search service is busy. Try again in a minute.");
            default:
                throw new WebSearchException(
                    WebSearchFailure.Unavailable,
                    $"The search service answered with status {(int)response.StatusCode}.");
        }
    }

    private static async Task<JsonDocument> ReadAnswer(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw TooLarge(maxBytes);
        }

        await using var body = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw TooLarge(maxBytes);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }

    private static WebSearchException TooLarge(long maxBytes) =>
        new(WebSearchFailure.TooLarge, $"The search service's answer was larger than this server reads ({maxBytes} bytes).");

    private static IEnumerable<WebSearchResult> ReadResults(JsonElement answer)
    {
        if (answer.ValueKind is not JsonValueKind.Object
            || !answer.TryGetProperty("results", out var results)
            || results.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("The answer has no results list.");
        }

        foreach (var result in results.EnumerateArray())
        {
            if (ReadResult(result) is { } read)
            {
                yield return read;
            }
        }
    }

    private static WebSearchResult? ReadResult(JsonElement result)
    {
        if (result.ValueKind is not JsonValueKind.Object
            || Text(result, "url") is not { Length: > 0 } address
            || !Uri.TryCreate(address, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return new WebSearchResult(Text(result, "title") ?? url.Host, url, Text(result, "content") ?? string.Empty);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString()?.Trim() : null;

    #endregion

    #region Constants

    public const string HttpClientName = "Drover.WebSearch.SearXng";

    #endregion
}
