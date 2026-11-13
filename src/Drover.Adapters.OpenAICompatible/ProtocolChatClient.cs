namespace Drover.Adapters.OpenAICompatible;

/// <summary>Uses the vendor client for wire handling and exposes only Drover-owned transport values.</summary>
internal sealed class ProtocolChatClient(HttpClient httpClient, ModelEndpoint endpoint, string apiKey, string modelId, ModelSettings settings, bool includeProviderData) : IModelChatClient
{
    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        var headers = new ProtocolHeaderCapture();
        using var client = CreateClient(headers);
        try
        {
            var response = await client.GetResponseAsync(request.Messages.Select(ModelChatMapping.ToChatMessage), ToChatOptions(request.Options), cancellation);
            return ModelChatMapping.ToModelResponse(response) with
            {
                Headers = headers.Headers,
                ProviderData = includeProviderData && response.RawRepresentation is OpenAI.Chat.ChatCompletion completion ? ReadData(ModelReaderWriter.Write(completion)) : null,
            };
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw WithHeaders(mapped, headers.Headers);
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var headers = new ProtocolHeaderCapture();
        using var client = CreateClient(headers);
        await using var updates = client.GetStreamingResponseAsync(request.Messages.Select(ModelChatMapping.ToChatMessage), ToChatOptions(request.Options), cancellation).GetAsyncEnumerator(cancellation);
        while (true)
        {
            var hasNext = false;
            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
            {
                throw WithHeaders(mapped, headers.Headers);
            }

            if (!hasNext)
            {
                yield break;
            }

            var update = updates.Current;
            yield return ModelChatMapping.ToModelUpdate(update) with
            {
                Headers = headers.Headers,
                ProviderData = includeProviderData && update.RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate chunk ? ReadData(ModelReaderWriter.Write(chunk)) : null,
            };
        }
    }

    public void Dispose() => httpClient.Dispose();

    #endregion

    #region Private Methods

    private ModelChatOptions WithDefaults(ModelChatOptions options) => options with
    {
        Temperature = options.Temperature ?? (float?)settings.Temperature,
        MaxOutputTokens = options.MaxOutputTokens ?? settings.MaxOutputTokens,
        ReasoningEffort = options.ReasoningEffort ?? settings.ReasoningEffort,
        ResponseFormat = options.ResponseFormat ?? (settings.StructuredOutput ? "json" : null),
    };

    private ChatOptions ToChatOptions(ModelChatOptions options)
    {
        var effective = WithDefaults(options);
        var effort = effective.ReasoningEffort?.ToLowerInvariant();
        if (effort is not ("max" or "minimal"))
        {
            return ModelChatMapping.ToChatOptions(effective);
        }

        var mapped = ModelChatMapping.ToChatOptions(effective with { ReasoningEffort = null });
        // The SDK's extensible effort preserves provider values missing from the framework enum; wire tests pin it.
#pragma warning disable OPENAI001
        mapped.RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
        {
            ReasoningEffortLevel = new OpenAI.Chat.ChatReasoningEffortLevel(effort),
        };
#pragma warning restore OPENAI001
        return mapped;
    }

    private IChatClient CreateClient(ProtocolHeaderCapture headers)
    {
        var options = CreateOptions(httpClient, endpoint);
        options.AddPolicy(headers, PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(modelId).AsIChatClient();
    }

    internal static OpenAIClientOptions CreateOptions(HttpClient client, ModelEndpoint destination) => new()
    {
        Endpoint = destination.BaseAddress.AbsoluteUri.EndsWith('/') ? destination.BaseAddress : new Uri($"{destination.BaseAddress.AbsoluteUri}/"),
        Transport = new HttpClientPipelineTransport(client),
        RetryPolicy = new ClientRetryPolicy(0),
        NetworkTimeout = Timeout.InfiniteTimeSpan,
    };

    private static ModelProviderException WithHeaders(ModelProviderException failure, IReadOnlyDictionary<string, string> headers) =>
        new(failure.Failure, failure.Message, failure.RetryAfter) { ResponseHeaders = headers };

    private static JsonElement ReadData(BinaryData data)
    {
        using var document = JsonDocument.Parse(data.ToMemory());
        return document.RootElement.Clone();
    }

    #endregion
}
