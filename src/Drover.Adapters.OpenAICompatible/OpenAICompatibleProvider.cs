namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Any endpoint that speaks the OpenAI chat protocol: a hosted provider or a local model server.
/// A key is optional. Capabilities are conservative until the operator says more.
/// </summary>
public sealed class OpenAICompatibleProvider(IHttpClientFactory httpClients) : IModelClientProvider
{
    #region Constants

    public const string ConnectionKind = "openai_compatible";

    public const string HttpClientName = "drover-openai-compatible";

    // The vendor client insists on a credential. Local servers ignore whatever arrives.
    private const string PlaceholderKey = "none";

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    #endregion

    #region Interface Implementations

    public IChatClient CreateChatClient(ModelConnection connection, string? apiKey, string modelId, ModelSettings settings)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = WithTrailingSlash(connection.BaseAddress),
            Transport = new HttpClientPipelineTransport(httpClients.CreateClient(HttpClientName)),

            // Retries belong to the resilience handler on the HttpClient, so they are not stacked.
            RetryPolicy = new ClientRetryPolicy(0),
        };

        var client = new OpenAIClient(new ApiKeyCredential(KeyOrPlaceholder(apiKey)), options);

        return new ErrorMappingChatClient(client.GetChatClient(modelId).AsIChatClient());
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelConnection connection,
        string? apiKey,
        CancellationToken cancellation) =>
        await ReadModels(connection, apiKey, cancellation) ?? [];

    public async Task<ConnectionTestResult> TestConnection(
        ModelConnection connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        try
        {
            var models = await ReadModels(connection, apiKey, cancellation);

            return models is null
                ? ConnectionTestResult.Ok("Connected. This endpoint does not list models, so type the model name.")
                : ConnectionTestResult.Ok($"Connected. The endpoint lists {models.Count} models.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    /// <summary>Returns null when the endpoint has no model list, which is normal for some servers.</summary>
    private async Task<List<ModelInfo>?> ReadModels(
        ModelConnection connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(WithTrailingSlash(connection.BaseAddress), "models"));

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellation);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.NotImplemented)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ErrorMappingChatClient.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);

                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellation);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellation);

            return ParseModels(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw ModelProviderException.FromNetwork(exception);
        }
    }

    private static List<ModelInfo> ParseModels(JsonElement root) =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? [.. data.EnumerateArray()
                .Select(model => model.TryGetProperty("id", out var id) ? id.GetString() : null)
                .OfType<string>()
                .Select(id => new ModelInfo(id, id, ModelCapabilities.Conservative, []))]
            : [];

    #endregion

    #region Private Methods

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    private static string KeyOrPlaceholder(string? apiKey) => string.IsNullOrEmpty(apiKey) ? PlaceholderKey : apiKey;

    #endregion
}
