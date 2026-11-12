namespace Drover.Adapters.Venice;

/// <summary>Venice request policy and response metadata over the shared, Drover-owned protocol seam.</summary>
internal sealed class VeniceChatClient(IModelChatClient inner, string modelId, ModelSettings settings) : IModelChatClient
{
    #region Private Fields

    private const string EncryptedReasoningPlaceholder = "[Some reasoning content is encrypted]";

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        try
        {
            var response = await inner.GetResponse(ShapeRequest(request), cancellation);
            var messages = response.Messages.Select(message => message with { Contents = MapContents(message.Contents) }).ToList();
            if (messages.Count > 0)
            {
                var last = messages[^1];
                messages[^1] = last with { Contents = AddCitations(last.Contents, response.ProviderData) };
            }

            return response with { Messages = messages, Balances = VeniceHeaders.ReadBalances(response.Headers), ProviderData = null };
        }
        catch (ModelProviderException failure)
        {
            throw WithReset(failure);
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await using var updates = inner.GetStreamingResponse(ShapeRequest(request), cancellation).GetAsyncEnumerator(cancellation);
        var balancesReported = false;
        var reasoningBuffer = string.Empty;
        while (true)
        {
            var hasNext = false;
            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (ModelProviderException failure)
            {
                throw WithReset(failure);
            }

            if (!hasNext)
            {
                if (reasoningBuffer.Length > 0)
                {
                    yield return new ModelChatUpdate([new ModelReasoning(reasoningBuffer)]);
                }

                yield break;
            }

            var update = updates.Current;
            List<ModelContent> contents = [];
            foreach (var content in update.Contents)
            {
                if (content is ModelReasoning reasoning)
                {
                    reasoningBuffer = $"{reasoningBuffer}{reasoning.Text}";
                    if (reasoningBuffer == EncryptedReasoningPlaceholder)
                    {
                        contents.Add(new ModelReasoning(string.Empty, true));
                        reasoningBuffer = string.Empty;
                    }
                    else if (!EncryptedReasoningPlaceholder.StartsWith(reasoningBuffer, StringComparison.Ordinal))
                    {
                        contents.Add(new ModelReasoning(reasoningBuffer));
                        reasoningBuffer = string.Empty;
                    }
                }
                else
                {
                    contents.Add(content);
                }
            }

            var balances = balancesReported ? [] : VeniceHeaders.ReadBalances(update.Headers);
            balancesReported |= balances.Count > 0;
            yield return update with { Contents = AddCitations(contents, update.ProviderData), Balances = balances, ProviderData = null };
        }
    }

    public void Dispose() => inner.Dispose();

    #endregion

    #region Private Methods

    private ModelChatRequest ShapeRequest(ModelChatRequest request)
    {
        var disableThinking = string.Equals(request.Options.ReasoningEffort ?? settings.ReasoningEffort, "None", StringComparison.OrdinalIgnoreCase);
        return request with
        {
            Options = request.Options with
            {
                ModelId = VeniceModelSuffix.Build(request.Options.ModelId ?? modelId, settings.WebSearch, disableThinking),
                ReasoningEffort = disableThinking ? null : request.Options.ReasoningEffort ?? settings.ReasoningEffort,
                Temperature = request.Options.Temperature ?? (float?)settings.Temperature,
                MaxOutputTokens = request.Options.MaxOutputTokens ?? settings.MaxOutputTokens,
            },
        };
    }

    private static IReadOnlyList<ModelContent> MapContents(IReadOnlyList<ModelContent> contents) =>
        [.. contents.Select(content => content is ModelReasoning reasoning && reasoning.Text.Trim() == EncryptedReasoningPlaceholder
            ? new ModelReasoning(string.Empty, true)
            : content)];

    private static IReadOnlyList<ModelContent> AddCitations(IReadOnlyList<ModelContent> contents, JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("venice_parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("web_search_citations", out var citations)
            || citations.ValueKind != JsonValueKind.Array)
        {
            return contents;
        }

        List<ModelCitation> links = [];
        foreach (var citation in citations.EnumerateArray())
        {
            if (citation.ValueKind == JsonValueKind.Object
                && citation.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var link) && link.Scheme is "http" or "https")
            {
                var title = citation.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                links.Add(new ModelCitation(title, link));
            }
        }

        return links.Count > 0 ? [.. contents, new ModelText(string.Empty) { Citations = links }] : contents;
    }

    private static ModelProviderException WithReset(ModelProviderException failure) =>
        new(failure.Failure, failure.Message, VeniceHeaders.WaitUntilReset(name => failure.ResponseHeaders.GetValueOrDefault(name), DateTimeOffset.UtcNow) ?? failure.RetryAfter);

    internal static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        if (exception is OperationCanceledException)
        {
            mapped = null!;
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var found = current switch
            {
                ModelProviderException failure => WithReset(failure),
                BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
                TimeoutRejectedException => ModelProviderException.FromTimeout(),
                HttpRequestException or HttpIOException => ModelProviderException.FromNetwork(),
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

    #endregion
}
