namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Reads <c>GET {address}/models</c>, the list both wires share: an object whose <c>data</c> holds entries with an
/// <c>id</c>. It says nothing else about a model.
/// </summary>
internal static class ProtocolModelList
{
    #region Public Methods

    /// <summary>Returns null when the endpoint has no model list, which is normal for some servers.</summary>
    public static async Task<List<string>?> ReadIds(
        HttpClient client,
        Uri address,
        string? apiKey,
        DiscoveryLimits limits,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(WithTrailingSlash(address), "models"));

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.NotImplemented)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);

                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, limits.MaxBytes, "model list", cancellation);

            return ParseIds(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion

    #region Private Methods

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    private static List<string> ParseIds(JsonElement root) =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? [.. data.EnumerateArray()
                .Select(model => model.TryGetProperty("id", out var id) ? id.GetString() : null)
                .OfType<string>()]
            : [];

    #endregion
}
