namespace Drover.Adapters.Gemini;

/// <summary>
/// One model on the provider's <c>generateContent</c> API, called with <see cref="HttpClient"/> and
/// <c>System.Text.Json</c>, so the thought signatures go back exactly as the provider needs them and no vendor type
/// exists to leave the adapter. The key goes in the <c>x-goog-api-key</c> header and nowhere else.
/// Sources (read 2026-11-27): https://ai.google.dev/api/generate-content (<c>POST /v1beta/models/{model}:generateContent</c>
/// and <c>:streamGenerateContent?alt=sse</c>) and https://ai.google.dev/gemini-api/docs/api-key (the header).
/// </summary>
internal sealed class GeminiChatClient(HttpClient httpClient, string apiKey, string modelId, ModelSettings settings) : IModelChatClient
{
    #region Private Fields

    public const string KeyHeader = "x-goog-api-key";

    /// <summary>
    /// Set once the provider refused a request that asked only for thought summaries, so the rest of this client's calls
    /// (a run's) leave the request out. A model that does not think refuses a thinking setting.
    /// </summary>
    private volatile bool thoughtsRefused;

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await Send(request, stream: false, cancellation);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            var answer = GeminiReply.Read(document.RootElement);

            return new ModelChatResponse([new ModelChatMessage("assistant", answer.Contents)])
            {
                Id = answer.ResponseId,
                ModelId = answer.ModelId,
                FinishReason = answer.FinishReason,
                Usage = answer.Usage,
            };
        }
        catch (Exception exception) when (GeminiFailures.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        HttpResponseMessage response;

        try
        {
            response = await Send(request, stream: true, cancellation);
        }
        catch (Exception exception) when (GeminiFailures.TryMap(exception, out var mapped))
        {
            throw mapped;
        }

        using (response)
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellation);
            await using var events = SseParser.Create(body).EnumerateAsync(cancellation).GetAsyncEnumerator(cancellation);
            ModelTokenUsage? usage = null;

            while (true)
            {
                GeminiAnswer? answer;

                try
                {
                    answer = await NextAnswer(events);
                }
                catch (Exception exception) when (GeminiFailures.TryMap(exception, out var mapped))
                {
                    throw mapped;
                }

                if (answer is null)
                {
                    break;
                }

                usage = answer.Usage ?? usage;

                if (answer.Contents.Count > 0 || answer.FinishReason is not null)
                {
                    yield return new ModelChatUpdate(answer.Contents)
                    {
                        ResponseId = answer.ResponseId,
                        ModelId = answer.ModelId,
                        Role = "assistant",
                        FinishReason = answer.FinishReason,
                    };
                }
            }

            if (usage is not null)
            {
                yield return new ModelChatUpdate([usage]) { Role = "assistant" };
            }
        }
    }

    public void Dispose() => httpClient.Dispose();

    #endregion

    #region Private Methods

    /// <summary>The next chunk that says something, or null at the end. A chunk that is an error is a failure of the call.</summary>
    private static async Task<GeminiAnswer?> NextAnswer(IAsyncEnumerator<SseItem<string>> events)
    {
        while (await events.MoveNextAsync())
        {
            if (string.IsNullOrWhiteSpace(events.Current.Data))
            {
                continue;
            }

            using var document = JsonDocument.Parse(events.Current.Data);

            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            {
                throw GeminiFailures.FromStreamError(error);
            }

            return GeminiReply.Read(document.RootElement);
        }

        return null;
    }

    private async Task<HttpResponseMessage> Send(ModelChatRequest request, bool stream, CancellationToken cancellation)
    {
        var askForThoughts = !thoughtsRefused;
        var response = await Post(request, stream, askForThoughts, cancellation);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var failure = await GeminiFailures.Read(response, cancellation);

        if (askForThoughts && failure.NamesThinking && GeminiRequest.AsksOnlyForThoughts(request, settings))
        {
            thoughtsRefused = true;
            response = await Post(request, stream, askForThoughts: false, cancellation);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            failure = await GeminiFailures.Read(response, cancellation);
        }

        throw failure.Exception;
    }

    private async Task<HttpResponseMessage> Post(ModelChatRequest request, bool stream, bool askForThoughts, CancellationToken cancellation)
    {
        var method = stream ? "streamGenerateContent?alt=sse" : "generateContent";
        var address = new Uri(GeminiProvider.BaseAddress, $"v1beta/models/{Uri.EscapeDataString(modelId)}:{method}");
        using var message = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(GeminiRequest.Build(request, settings, askForThoughts).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation(KeyHeader, apiKey);

        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }

    #endregion
}
