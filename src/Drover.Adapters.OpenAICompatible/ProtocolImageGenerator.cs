namespace Drover.Adapters.OpenAICompatible;

/// <summary>Generates one image through the compatible images protocol.</summary>
internal sealed class ProtocolImageGenerator(HttpClient client)
{
    #region Private Fields

    private const decimal MaxReportedCost = 1_000m;

    private static readonly byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    #endregion

    #region Public Methods

    public async Task<ModelImageResult> Generate(
        ModelEndpoint connection,
        string? apiKey,
        ModelImageRequest generation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(WithTrailingSlash(connection.BaseAddress), "images/generations"));
        request.Content = JsonContent.Create(new
        {
            prompt = generation.Prompt,
            model = generation.Model,
            n = 1,
            size = generation.Size,
            output_format = "png",
        });

        ProtocolKey.Apply(request, connection.BaseAddress, apiKey);

        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);
                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            var maxResponseBytes = checked((generation.MaxBytes * 2) + 4096);
            using var document = await BoundedResponse.ReadJson(
                response.Content,
                maxResponseBytes,
                "image answer",
                cancellationToken);
            return Read(document.RootElement, generation);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion

    #region Response Reading

    private static ModelImageResult Read(JsonElement root, ModelImageRequest request)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() != 1
            || !data[0].TryGetProperty("b64_json", out var encoded)
            || encoded.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("The image answer has no image.");
        }

        var bytes = Convert.FromBase64String(encoded.GetString()!);

        if (bytes.Length == 0 || bytes.Length > request.MaxBytes)
        {
            throw ModelProviderException.FromLimit("image answer");
        }

        if (!bytes.AsSpan().StartsWith(pngSignature))
        {
            throw new FormatException("The image answer is not a PNG.");
        }

        return new ModelImageResult(bytes, "image/png", request.Model)
        {
            Usage = ReadUsage(root),
        };
    }

    private static ModelImageUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new ModelImageUsage();
        }

        var currency = ReadString(usage, "currency")?.Trim().ToUpperInvariant() ?? "USD";
        var validCurrency = currency.Length is >= 3 and <= 8 && currency.All(char.IsAsciiLetterUpper);
        return new ModelImageUsage
        {
            InputTokens = ReadCount(usage, "input_tokens"),
            OutputTokens = ReadCount(usage, "output_tokens"),
            Cost = validCurrency ? ReadCost(usage) : null,
            Currency = validCurrency ? currency : "USD",
        };
    }

    private static int ReadCount(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt32(out var count) && count >= 0 ? count : 0;

    private static decimal? ReadCost(JsonElement element) =>
        element.TryGetProperty("cost", out var value) && value.ValueKind is JsonValueKind.Number
        && value.TryGetDecimal(out var cost) && cost is >= 0 and <= MaxReportedCost ? cost : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion

    #region Private Methods

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion
}
