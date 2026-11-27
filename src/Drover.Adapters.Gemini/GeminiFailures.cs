namespace Drover.Adapters.Gemini;

/// <summary>A refused call, in Drover's terms, and whether the refusal was about the thinking settings.</summary>
internal sealed record GeminiFailure(ModelProviderException Exception, bool NamesThinking);

/// <summary>
/// Turns the provider's errors into Drover's. The provider's own message is never repeated: it can quote the
/// conversation. Shapes (read 2026-11-27): an error is <c>{"error":{"code","message","status","details":[...]}}</c>
/// (https://ai.google.dev/gemini-api/docs/troubleshooting). A wrong key is HTTP 400 INVALID_ARGUMENT whose
/// <c>details</c> carry an ErrorInfo with <c>reason: "API_KEY_INVALID"</c>, a missing key is 403 PERMISSION_DENIED and a
/// key sent as a bearer token is 401: reproduced against the live service by third parties on 2026-11-09 (unconfirmed
/// here, no key was available): https://aitoolsrank.io/fix/gemini-api-key-not-valid/. A 429 carries no
/// <c>Retry-After</c> header; the wait is a <c>google.rpc.RetryInfo</c> detail with <c>retryDelay</c> such as "34.4s"
/// (https://github.com/vercel/ai/issues/18627). Too long an input is 400 "The input token count exceeds the maximum
/// number of tokens allowed" (https://github.com/google-gemini/gemini-cli/issues/15184).
/// </summary>
internal static class GeminiFailures
{
    #region Private Fields

    private const int MaxBody = 65536;

    #endregion

    #region Public Methods

    /// <summary>Reads the refusal and disposes the response.</summary>
    public static async Task<GeminiFailure> Read(HttpResponseMessage response, CancellationToken cancellation)
    {
        using (response)
        {
            var body = await ReadBody(response.Content, cancellation);

            return Describe((int)response.StatusCode, body);
        }
    }

    public static GeminiFailure Describe(int status, string? body)
    {
        JsonElement error = default;
        using var document = Parse(body);

        if (document is not null && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("error", out var found) && found.ValueKind == JsonValueKind.Object)
        {
            error = found;
        }

        var message = Text(error, "message") ?? string.Empty;
        var thinking = status == 400 && message.Contains("thinking", StringComparison.OrdinalIgnoreCase);

        if (status == 400 && HasReason(error, "API_KEY_INVALID"))
        {
            return new(ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized), false);
        }

        if (status == 400 && message.Contains("input token count", StringComparison.OrdinalIgnoreCase)
            && message.Contains("exceeds", StringComparison.OrdinalIgnoreCase))
        {
            return new(new ModelProviderException(ModelFailure.TooLong, "The provider says this conversation is longer than the model can read."), false);
        }

        return new(ModelProviderException.FromStatus(status, status == 429 ? RetryDelay(error) : null), thinking);
    }

    /// <summary>The failure Drover understands anywhere in the exception chain, without the vendor's own exception.</summary>
    public static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        if (exception is OperationCanceledException)
        {
            mapped = null!;
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutboundBlockedException blocked)
            {
                mapped = ModelProviderException.FromRefusal(blocked.Message);
                return true;
            }
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var found = current switch
            {
                ModelProviderException failure => failure,
                BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
                TimeoutRejectedException => ModelProviderException.FromTimeout(),
                HttpRequestException or HttpIOException => ModelProviderException.FromNetwork(),
                JsonException => ModelProviderException.FromMalformedResponse(),
                _ => null,
            };

            if (found is not null)
            {
                mapped = found;
                return true;
            }
        }

        mapped = null!;
        return false;
    }

    /// <summary>A failure the provider reports inside a stream, as <c>{"error":{...}}</c>.</summary>
    public static ModelProviderException FromStreamError(JsonElement error) =>
        Describe(error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : 500, error.GetRawText().Length <= MaxBody ? WrapError(error) : null).Exception;

    #endregion

    #region Private Methods

    private static string WrapError(JsonElement error) => $$"""{"error":{{error.GetRawText()}}}""";

    private static async Task<string?> ReadBody(HttpContent content, CancellationToken cancellation)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellation);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;

            while (buffer.Length < MaxBody && (read = await stream.ReadAsync(chunk, cancellation)) > 0)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellation);
            }

            return buffer.Length >= MaxBody ? null : Encoding.UTF8.GetString(buffer.ToArray());
        }
        // An unreadable error body leaves the ordinary status mapping, which is all the caller needs.
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    private static JsonDocument? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        // A body that is not JSON (a proxy's page) says nothing; the status does.
#pragma warning disable DRV0005
        catch (JsonException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    private static bool HasReason(JsonElement error, string reason) =>
        Details(error).Any(detail => Text(detail, "reason") == reason);

    private static TimeSpan? RetryDelay(JsonElement error)
    {
        foreach (var detail in Details(error))
        {
            if (Text(detail, "@type")?.EndsWith("google.rpc.RetryInfo", StringComparison.Ordinal) == true
                && Text(detail, "retryDelay") is { } delay && delay.EndsWith('s')
                && double.TryParse(delay[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }

    private static List<JsonElement> Details(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Where(detail => detail.ValueKind == JsonValueKind.Object)]
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion
}
