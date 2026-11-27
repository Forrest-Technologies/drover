namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Reads <c>GET {address}/models</c>, the list both wires share: an object whose <c>data</c> holds entries with an
/// <c>id</c>. <see cref="ReadIds"/> keeps only the id; <see cref="Read{T}"/> hands each entry to a projection, for a
/// caller that reads more of what the entry says about a model.
/// </summary>
internal static class ProtocolModelList
{
    #region Public Methods

    /// <summary>Returns null when the endpoint has no model list, which is normal for some servers.</summary>
    public static Task<List<string>?> ReadIds(
        HttpClient client,
        Uri address,
        string? apiKey,
        DiscoveryLimits limits,
        CancellationToken cancellation) =>
        Read(client, address, apiKey, limits, ReadId, cancellation);

    /// <summary>
    /// Reads the list and hands each entry to <paramref name="project"/> while the answer is still open, so a caller that
    /// wants more than the id can take it. An entry the projection returns null for is left out. Returns null when the
    /// endpoint has no model list.
    /// </summary>
    public static async Task<List<T>?> Read<T>(
        HttpClient client,
        Uri address,
        string? apiKey,
        DiscoveryLimits limits,
        Func<JsonElement, T?> project,
        CancellationToken cancellation)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(WithTrailingSlash(address), "models"));

        ProtocolKey.Apply(request, address, apiKey);

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

            return Parse(document.RootElement, project);
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

    private static string? ReadId(JsonElement model) => model.TryGetProperty("id", out var id) ? id.GetString() : null;

    private static List<T> Parse<T>(JsonElement root, Func<JsonElement, T?> project)
        where T : class =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? [.. data.EnumerateArray().Select(project).OfType<T>()]
            : [];

    #endregion
}
