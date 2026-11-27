namespace Drover.Adapters.Anthropic;

/// <summary>
/// Reads the provider's model list (<c>GET /v1/models</c>, every page) and one model (<c>GET /v1/models/{id}</c>), bounded
/// by the deployment's discovery limits, and turns each entry into Drover's model record. Nothing about a model is
/// written here: its context size, its longest answer and what it can do all come from the provider, and what the
/// entry does not say is false. The list carries no prices, so they stay unknown.
/// Sources (read 2026-11-27): https://platform.claude.com/docs/en/api/models/list (<c>data</c>, <c>has_more</c>,
/// <c>last_id</c>, <c>after_id</c>, <c>limit</c> up to 1000; each entry's <c>id</c>, <c>display_name</c>,
/// <c>max_input_tokens</c>, <c>max_tokens</c> and <c>capabilities</c>, every one nullable),
/// https://platform.claude.com/docs/en/api/models/retrieve, and https://platform.claude.com/docs/en/api/versioning
/// (<c>anthropic-version</c> is required on every request; <c>2026-10-21</c> is the current version).
/// </summary>
internal static class AnthropicModels
{
    #region Private Fields

    /// <summary>The API version this adapter speaks, the one the vendor package sends on its own calls too.</summary>
    public const string ApiVersion = "2026-10-21";

    /// <summary>The largest page the list endpoint gives.</summary>
    private const int PageSize = 1000;

    /// <summary>The effort levels the model list can name, in the order a person picks from.</summary>
    private static readonly string[] effortLevels = ["low", "medium", "high", "xhigh", "max"];

    #endregion

    #region Public Methods

    /// <summary>Every model of every page, stopping once more models came back than the deployment accepts.</summary>
    public static async Task<IReadOnlyList<ModelInfo>> List(
        HttpClient client,
        AnthropicDestination destination,
        DiscoveryLimits limits,
        CancellationToken cancellation)
    {
        List<ModelInfo> models = [];
        string? after = null;

        while (true)
        {
            var path = after is null ? $"v1/models?limit={PageSize}" : $"v1/models?limit={PageSize}&after_id={Uri.EscapeDataString(after)}";
            using var page = await Read(client, destination, path, limits, cancellation);
            var root = page.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind is not JsonValueKind.Array)
            {
                throw ModelProviderException.FromMalformedResponse();
            }

            models.AddRange(data.EnumerateArray().Select(ToModelInfo).OfType<ModelInfo>());
            after = root.TryGetProperty("last_id", out var last) && last.ValueKind is JsonValueKind.String ? last.GetString() : null;

            if (models.Count > limits.MaxModels || after is null
                || !root.TryGetProperty("has_more", out var more) || more.ValueKind is not JsonValueKind.True)
            {
                return models;
            }
        }
    }

    /// <summary>One page of one model: the call a connection test makes, which is not billed.</summary>
    public static async Task CheckKey(HttpClient client, AnthropicDestination destination, DiscoveryLimits limits, CancellationToken cancellation)
    {
        using var page = await Read(client, destination, "v1/models?limit=1", limits, cancellation);
    }

    /// <summary>One model, as the list would give it.</summary>
    public static async Task<ModelInfo> Find(
        HttpClient client,
        AnthropicDestination destination,
        string modelId,
        DiscoveryLimits limits,
        CancellationToken cancellation)
    {
        using var entry = await Read(client, destination, $"v1/models/{Uri.EscapeDataString(modelId)}", limits, cancellation);
        return ToModelInfo(entry.RootElement) ?? throw ModelProviderException.FromMalformedResponse();
    }

    /// <summary>Puts the version, the credential and the destination's own headers on a request.</summary>
    public static void AddHeaders(HttpRequestMessage request, AnthropicDestination destination)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", destination.Credential);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        AnthropicHeaderReplacement.Replace(request, destination.Headers);
    }

    #endregion

    #region Reading

    private static async Task<JsonDocument> Read(
        HttpClient client,
        AnthropicDestination destination,
        string path,
        DiscoveryLimits limits,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(destination.Address, path));
        AddHeaders(request, destination);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw await AnthropicFailures.FromResponse(response, limits.MaxBytes, cancellation);
            }

            return await BoundedResponse.ReadJson(response.Content, limits.MaxBytes, "model list", cancellation);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (exception is not ModelProviderException && AnthropicFailures.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion

    #region Model Records

    /// <summary>An entry with no id is not a model; anything else becomes one, with what its entry says.</summary>
    private static ModelInfo? ToModelInfo(JsonElement entry)
    {
        if (entry.ValueKind is not JsonValueKind.Object || String(entry, "id") is not { Length: > 0 } id)
        {
            return null;
        }

        var capabilities = entry.TryGetProperty("capabilities", out var found) && found.ValueKind is JsonValueKind.Object ? found : default;
        var context = Number(entry, "max_input_tokens");
        var pdf = Supported(capabilities, "pdf_input");

        return new ModelInfo(
            id,
            String(entry, "display_name") is { Length: > 0 } name ? name : id,
            new ModelCapabilities
            {
                ContextTokens = context,
                MaxOutputTokens = Number(entry, "max_tokens"),
                ToolCalling = true,
                Streaming = true,
                Vision = Supported(capabilities, "image_input"),
                Reasoning = Thinks(capabilities),
                ReasoningEfforts = Efforts(capabilities),
                StructuredOutput = Supported(capabilities, "structured_outputs"),
                PdfInput = pdf,
                PdfMaxPages = pdf ? AnthropicLimits.PdfPagesFor(context) : null,
                PdfMaxBytes = pdf ? AnthropicLimits.MaxPdfBytes : null,
            },
            []);
    }

    /// <summary>
    /// The adapter drives thinking only as the provider's current models take it, adaptive with the effort beside it,
    /// so a model that does not take adaptive thinking is not offered reasoning.
    /// </summary>
    private static bool Thinks(JsonElement capabilities) =>
        Child(capabilities, "thinking") is { } thinking && Supported(thinking, "supported", direct: true)
        && Child(thinking, "types") is { } types && Supported(types, "adaptive");

    private static IReadOnlyList<string> Efforts(JsonElement capabilities) =>
        Child(capabilities, "effort") is { } effort && Supported(effort, "supported", direct: true)
            ? [.. effortLevels.Where(level => Supported(effort, level))]
            : [];

    /// <summary>
    /// A capability is <c>{ "supported": true }</c> under its name; with <paramref name="direct"/>, the named property
    /// is the boolean itself. Anything missing or of another shape is false.
    /// </summary>
    private static bool Supported(JsonElement parent, string name, bool direct = false)
    {
        if (parent.ValueKind is not JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return false;
        }

        if (direct)
        {
            return value.ValueKind is JsonValueKind.True;
        }

        return value.ValueKind is JsonValueKind.Object && value.TryGetProperty("supported", out var supported)
            && supported.ValueKind is JsonValueKind.True;
    }

    private static JsonElement? Child(JsonElement parent, string name) =>
        parent.ValueKind is JsonValueKind.Object && parent.TryGetProperty(name, out var child) && child.ValueKind is JsonValueKind.Object
            ? child
            : null;

    private static string? String(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0
            ? number
            : null;

    #endregion
}
