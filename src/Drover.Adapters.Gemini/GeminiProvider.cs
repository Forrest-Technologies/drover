namespace Drover.Adapters.Gemini;

/// <summary>
/// Google Gemini with an API key, on the <c>generateContent</c> API. The address is fixed. There is no plan sign-in for
/// this provider: a key is the only way in. The model list carries token limits and whether a model thinks, and no
/// prices, so a model's prices stay unknown until an operator adds them.
/// Sources (read 2026-11-27): https://ai.google.dev/api/models (<c>GET /v1beta/models</c> with <c>pageSize</c> and
/// <c>pageToken</c>; a model has <c>name</c>, <c>displayName</c>, <c>inputTokenLimit</c>, <c>outputTokenLimit</c>,
/// <c>supportedGenerationMethods</c> and <c>thinking</c>), https://ai.google.dev/gemini-api/docs/api-key (the key in
/// <c>x-goog-api-key</c>) and https://aistudio.google.com/docs/thinking (the thinking levels). The list says which
/// models think but not which levels each takes, so a thinking model offers <c>low</c> and <c>high</c>, which every
/// level table on that page shares (decided 2026-11-26).
/// </summary>
public sealed class GeminiProvider(IHttpClientFactory httpClients) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "gemini";

    public const string HttpClientName = "drover-gemini";

    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/");

    /// <summary>The provider returns at most this many models in a page.</summary>
    private const int PageSize = 1000;

    /// <summary>A loop guard: no real list has this many pages.</summary>
    private const int MaxPages = 100;

    private static readonly string[] thinkingLevels = ["low", "high"];

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => BaseAddress;

    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        "Google Gemini",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: "Paste a Gemini API key. A personal plan cannot be used here.",
        ReportsCapabilities: true);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new GeminiChatClient(httpClients.CreateClient(HttpClientName), apiKey, modelId, settings);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        List<ModelInfo> models = [];
        string? token = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var path = token is null
                ? $"v1beta/models?pageSize={PageSize}"
                : $"v1beta/models?pageSize={PageSize}&pageToken={Uri.EscapeDataString(token)}";
            using var document = await Get(path, apiKey, connection.Limits, cancellation);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw ModelProviderException.FromMalformedResponse();
            }

            if (root.TryGetProperty("models", out var listed) && listed.ValueKind == JsonValueKind.Array)
            {
                models.AddRange(listed.EnumerateArray().Select(ToModel).OfType<ModelInfo>());
            }

            if (models.Count > connection.Limits.MaxModels)
            {
                throw ModelProviderException.FromLimit("model list");
            }

            var next = root.TryGetProperty("nextPageToken", out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;

            if (string.IsNullOrEmpty(next) || next == token)
            {
                break;
            }

            token = next;
        }

        return models;
    }

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste a Gemini API key to finish setup.");
        }

        try
        {
            using var firstPage = await Get("v1beta/models?pageSize=1", apiKey, connection.Limits, cancellation);

            return ConnectionTestResult.Ok("Connected to Gemini.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>A model that cannot generate content (an embedding or an image model) is not listed. No name is looked at.</summary>
    private static ModelInfo? ToModel(JsonElement model)
    {
        if (model.ValueKind != JsonValueKind.Object || !CanGenerateContent(model) || Text(model, "name") is not { } name)
        {
            return null;
        }

        var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
        var thinks = model.TryGetProperty("thinking", out var thinking) && thinking.ValueKind == JsonValueKind.True;

        return id.Length == 0
            ? null
            : new ModelInfo(id, Text(model, "displayName") is { Length: > 0 } display ? display : id, new ModelCapabilities
            {
                ContextTokens = Limit(model, "inputTokenLimit"),
                MaxOutputTokens = Limit(model, "outputTokenLimit"),
                ToolCalling = true,
                Streaming = true,
                Vision = true,
                StructuredOutput = true,
                Reasoning = thinks,
                ReasoningEfforts = thinks ? thinkingLevels : [],
            }, []);
    }

    private static bool CanGenerateContent(JsonElement model) =>
        model.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array
        && methods.EnumerateArray().Any(method => method.ValueKind == JsonValueKind.String && method.GetString() == "generateContent");

    private static int? Limit(JsonElement model, string name) =>
        model.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count > 0
            ? count
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<JsonDocument> Get(string path, string apiKey, DiscoveryLimits limits, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, path));
        request.Headers.TryAddWithoutValidation(GeminiChatClient.KeyHeader, apiKey);

        try
        {
            var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw (await GeminiFailures.Read(response, cancellation)).Exception;
            }

            using (response)
            {
                return await BoundedResponse.ReadJson(response.Content, limits.MaxBytes, "answer", cancellation);
            }
        }
        catch (Exception exception) when (GeminiFailures.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion
}
