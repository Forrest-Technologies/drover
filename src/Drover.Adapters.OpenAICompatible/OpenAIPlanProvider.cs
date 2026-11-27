namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// A person's own ChatGPT plan, on the provider's Responses API with the access token the person signed in for
/// (<see cref="OpenAIPlanSignIn"/>). Each person signs in with their own plan; the credential a call carries is always
/// the person's own, chosen by the engine. Only <c>GET models</c> and <c>POST responses</c> are called, both on the
/// configured <see cref="OpenAIPlanOptions.ApiAddress"/>; every call streams and is shaped as the plan requires
/// (<see cref="PlanRequestShaping"/>).
/// Sources (read 2026-11-27): https://developers.openai.com/siwc/llms-full.txt, "Models and inference" (the model list
/// is <c>{"models":[{slug, display_name, visibility}]}</c>, shown where <c>visibility</c> is <c>list</c>, in the
/// provider's order) and "Preview limitations" (images and files where the model takes them; no image generation, file
/// search, code interpreter, computer use, hosted connectors or tool search; no audio input).
/// A person who signed in by code (<see cref="OpenAIPlanCodeOptions"/>) holds a token for the address the provider's own
/// command line tool calls: their calls go to <c>codex/responses</c> and <c>codex/models</c> on
/// <see cref="OpenAIPlanCodeOptions.ApiAddress"/>, with that flow's headers and the account the token names. The flow
/// travels with each call in <see cref="ModelEndpoint.SignInDetails"/>, so one connection serves people signed in either way.
/// </summary>
public sealed class OpenAIPlanProvider(IHttpClientFactory httpClients, IOptionsMonitor<OpenAIPlanOptions> options) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "openai_signin";

    public const string DisplayName = "ChatGPT plan";

    public const string HttpClientName = "drover-openai-plan";

    /// <summary>
    /// Where a person reviews and limits what apps use of their plan. The provider asks apps to link it from their
    /// usage pages and from a usage-limit message ("Tracking usage", "UI/UX guidelines").
    /// </summary>
    public static readonly Uri UsagePage = new("https://chatgpt.com/settings/usage");

    private static readonly IReadOnlyDictionary<string, string> noHeaders = new Dictionary<string, string>();

    /// <summary>
    /// What a listed model starts with. The list names a model and nothing of what it does, so it starts with what the
    /// plan's Responses wire carries, as the key kind's models do, and nothing the plan refuses: no image generation,
    /// no hosted web search, no speech. Context size and prices stay unknown; a plan has no price per token.
    /// </summary>
    private static readonly ModelCapabilities planCapabilities = new()
    {
        ToolCalling = true,
        Streaming = true,
        Vision = true,
        StructuredOutput = true,
        Reasoning = true,
        ReasoningEfforts = ["low", "medium", "high"],
        WebSearch = false,
        ImageGeneration = false,
        SpeechToText = false,
        TextToSpeech = false,
        RealtimeVoice = false,
    };

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => options.CurrentValue.ApiAddress;

    /// <summary>Read for each request, so a changed return address in the settings needs no restart.</summary>
    public ModelProviderDescriptor Descriptor => new(
        ConnectionKind,
        DisplayName,
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: false,
        SignInName: "ChatGPT",
        UsagePage: UsagePage,
        SignInReturn: options.CurrentValue.RedirectUri);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        if (!IsByCode(connection))
        {
            return new ResponsesChatClient(
                httpClients.CreateClient(HttpClientName),
                new ResponsesDestination(options.CurrentValue.ApiAddress, apiKey, noHeaders),
                modelId,
                settings,
                ResponsesRules.Plan);
        }

        var code = options.CurrentValue.Code;
        return new ResponsesChatClient(
            httpClients.CreateClient(HttpClientName),
            new ResponsesDestination(new Uri(WithTrailingSlash(code.ApiAddress), "codex/"), apiKey, CodeHeaders(code, connection, apiKey)),
            modelId,
            settings,
            ResponsesRules.PlanByCode);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        [.. (await ReadModels(connection, apiKey, cancellation)).Select(model => new ModelInfo(model.Slug, model.Name, planCapabilities, []))];

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Sign in with your ChatGPT plan to finish setup.");
        }

        try
        {
            await ReadModels(connection, apiKey, cancellation);
            return ConnectionTestResult.Ok("Connected to your ChatGPT plan.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    private async Task<List<PlanModel>> ReadModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        using var request = ModelListRequest(connection, apiKey);

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);
                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, connection.Limits.MaxBytes, "model list", cancellation);
            return ParseModels(document.RootElement);
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

    /// <summary>
    /// The model list request of the connection's flow. A sign-in by code asks its own address with the tool version and
    /// the flow's headers, and asks for JSON: the flow's <c>accept</c> header is for runs, which stream.
    /// </summary>
    private HttpRequestMessage ModelListRequest(ModelEndpoint connection, string? apiKey)
    {
        if (!IsByCode(connection))
        {
            return new HttpRequestMessage(HttpMethod.Get, new Uri(WithTrailingSlash(options.CurrentValue.ApiAddress), "models"));
        }

        var code = options.CurrentValue.Code;
        var address = new Uri(WithTrailingSlash(code.ApiAddress), $"codex/models?client_version={Uri.EscapeDataString(code.ClientVersion)}");
        var request = new HttpRequestMessage(HttpMethod.Get, address);

        foreach (var (name, value) in CodeHeaders(code, connection, apiKey))
        {
            if (!name.Equals("accept", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    /// <summary>True when the person's sign-in was made by code (<see cref="OpenAIPlanSignIn.FlowDetail"/>).</summary>
    private static bool IsByCode(ModelEndpoint connection) =>
        connection.SignInDetails.GetValueOrDefault(OpenAIPlanSignIn.FlowDetail) == OpenAIPlanSignIn.CodeFlow;

    /// <summary>
    /// The flow's configured headers and the account the token names. A token that names none is sent with the
    /// account its sign-in recorded.
    /// </summary>
    private static Dictionary<string, string> CodeHeaders(OpenAIPlanCodeOptions code, ModelEndpoint connection, string? apiKey)
    {
        Dictionary<string, string> headers = new(code.Headers, StringComparer.OrdinalIgnoreCase);

        if ((PlanAccount.Of(apiKey) ?? connection.SignInDetails.GetValueOrDefault(OpenAIPlanSignIn.AccountDetail)) is { Length: > 0 } account)
        {
            headers["chatgpt-account-id"] = account;
        }

        return headers;
    }

    /// <summary>The entries meant for a picker, in the provider's order. One without a slug, or a slug seen before, is left out.</summary>
    private static List<PlanModel> ParseModels(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind is not JsonValueKind.Array)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        HashSet<string> seen = [with(StringComparer.Ordinal)];
        List<PlanModel> listed = [];

        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind is not JsonValueKind.Object || Text(model, "visibility") != "list" || Text(model, "slug") is not { Length: > 0 } slug || !seen.Add(slug))
            {
                continue;
            }

            listed.Add(new PlanModel(slug, Text(model, "display_name") is { Length: > 0 } name ? name : slug));
        }

        return listed;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion

    #region Nested Types

    private sealed record PlanModel(string Slug, string Name);

    #endregion
}
