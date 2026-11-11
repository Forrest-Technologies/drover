namespace Drover.Adapters.Venice;

/// <summary>
/// Venice as a named provider. The address is fixed and only an inference key is needed.
/// Model discovery needs no key, so the model picker works before the key is saved.
/// </summary>
public sealed class VeniceProvider(IHttpClientFactory httpClients) : IModelClientProvider
{
    #region Constants

    public const string ConnectionKind = "venice";

    public const string HttpClientName = "drover-venice";

    public static readonly Uri BaseAddress = new("https://api.venice.ai/api/v1/");

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    #endregion

    #region Interface Implementations

    public IChatClient CreateChatClient(ModelConnection connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        var headers = new VeniceHeaderCapture();
        var options = new OpenAIClientOptions
        {
            Endpoint = BaseAddress,
            Transport = new HttpClientPipelineTransport(httpClients.CreateClient(HttpClientName)),

            // Retries belong to the resilience handler on the HttpClient, so they are not stacked.
            RetryPolicy = new ClientRetryPolicy(0),
        };

        options.AddPolicy(headers, PipelinePosition.PerCall);

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);

        return new VeniceChatClient(client.GetChatClient(modelId).AsIChatClient(), modelId, settings, headers);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelConnection connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        using var models = await Get("models?type=text", null, cancellation);

        // The trait list only adds the provider's picks. The models are usable without it.
        var traits = await TryGetTraits(cancellation);

        return VeniceModelCatalog.Parse(models.RootElement, traits?.RootElement);
    }

    public async Task<ConnectionTestResult> TestConnection(
        ModelConnection connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste a Venice inference key to finish setup.");
        }

        try
        {
            using var limits = await Get("api_keys/rate_limits", apiKey, cancellation);

            return ReadKeyStatus(limits.RootElement);
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Private Methods

    private static ConnectionTestResult ReadKeyStatus(JsonElement root)
    {
        var data = root.GetProperty("data");

        if (data.TryGetProperty("accessPermitted", out var permitted) && permitted.ValueKind == JsonValueKind.False)
        {
            return ConnectionTestResult.Failed(
                ModelFailure.Rejected,
                "Venice accepted the key, but it is not allowed to run models. Check the key in your Venice settings.");
        }

        var balances = data.TryGetProperty("balances", out var found) && found.ValueKind == JsonValueKind.Object
            ? found.EnumerateObject()
                .Where(balance => balance.Value.ValueKind == JsonValueKind.Number)
                .Select(balance => new ModelBalance(balance.Name.ToLowerInvariant(), balance.Value.GetDecimal()))
                .ToList()
            : [];

        return ConnectionTestResult.Ok("Connected to Venice.", balances);
    }

    private async Task<JsonDocument?> TryGetTraits(CancellationToken cancellation)
    {
        try
        {
            return await Get("models/traits?type=text", null, cancellation);
        }
        catch (ModelProviderException)
        {
            return null;
        }
    }

    private async Task<JsonDocument> Get(string path, string? apiKey, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, path));

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                throw ModelProviderException.FromStatus((int)response.StatusCode, ReadWait(response));
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellation);

            return await JsonDocument.ParseAsync(body, cancellationToken: cancellation);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw ModelProviderException.FromNetwork(exception);
        }
    }

    private static TimeSpan? ReadWait(HttpResponseMessage response) =>
        response.Headers.TryGetValues(VeniceHeaders.RateLimitReset, out var values)
            ? VeniceHeaders.WaitUntilReset(values.FirstOrDefault(), DateTimeOffset.UtcNow)
            : null;

    #endregion
}
