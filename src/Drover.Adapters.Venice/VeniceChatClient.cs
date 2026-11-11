namespace Drover.Adapters.Venice;

/// <summary>
/// Wraps the compatible chat client with what is particular to Venice: the model suffix, the reasoning
/// placeholder, web-search citations, balance headers, and failures in Drover's terms.
/// </summary>
internal sealed class VeniceChatClient(
    IChatClient inner,
    string modelId,
    ModelSettings settings,
    VeniceHeaderCapture headers) : DelegatingChatClient(inner)
{
    #region Constants

    private const string EncryptedReasoningPlaceholder = "[Some reasoning content is encrypted]";

    #endregion

    #region Interface Implementations

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await base.GetResponseAsync(messages, ShapeOptions(options), cancellationToken);

            MapResponse(response);
            StripVendorTypes(response);

            return response;
        }
        catch (Exception exception) when (TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var updates = base.GetStreamingResponseAsync(messages, ShapeOptions(options), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        var balancesReported = false;

        while (true)
        {
            bool hasNext;

            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception) when (TryMap(exception, out var mapped))
            {
                throw mapped;
            }

            if (!hasNext)
            {
                yield break;
            }

            var update = updates.Current;

            MapUpdate(update);
            StripVendorTypes(update);

            if (!balancesReported && headers.Balances.Count > 0)
            {
                update.AdditionalProperties ??= [];
                update.AdditionalProperties[ModelResponseKeys.Balances] = headers.Balances;
                balancesReported = true;
            }

            yield return update;
        }
    }

    #endregion

    #region Request Shaping

    /// <summary>
    /// Puts the provider options in the model name. The neutral reasoning setting "none" becomes the
    /// documented switch that turns thinking off; every other effort is sent as the standard field.
    /// </summary>
    private ChatOptions ShapeOptions(ChatOptions? options)
    {
        var shaped = options?.Clone() ?? new ChatOptions();
        var disableThinking = shaped.Reasoning?.Effort == ReasoningEffort.None;

        if (disableThinking)
        {
            shaped.Reasoning = null;
        }

        shaped.ModelId = VeniceModelSuffix.Build(shaped.ModelId ?? modelId, settings.WebSearch, disableThinking);

        return shaped;
    }

    #endregion

    #region Response Mapping

    private void MapResponse(ChatResponse response)
    {
        var contents = response.Messages.SelectMany(message => message.Contents).ToList();

        MarkEncryptedReasoning(contents);

        if (response.RawRepresentation is ChatCompletion completion)
        {
            AddCitations(response.Messages.LastOrDefault()?.Contents, ReadCitations(completion));
        }

        if (headers.Balances.Count > 0)
        {
            response.AdditionalProperties ??= [];
            response.AdditionalProperties[ModelResponseKeys.Balances] = headers.Balances;
        }
    }

    private static void MapUpdate(ChatResponseUpdate update)
    {
        MarkEncryptedReasoning(update.Contents);

        if (update.RawRepresentation is StreamingChatCompletionUpdate chunk)
        {
            AddCitations(update.Contents, ReadCitations(chunk));
        }
    }

    /// <summary>Models that hide their thinking send a fixed placeholder. That is "not available", not text.</summary>
    private static void MarkEncryptedReasoning(IEnumerable<AIContent> contents)
    {
        foreach (var reasoning in contents.OfType<TextReasoningContent>())
        {
            if (reasoning.Text?.Trim() == EncryptedReasoningPlaceholder)
            {
                reasoning.Text = string.Empty;
                reasoning.AdditionalProperties ??= [];
                reasoning.AdditionalProperties[ModelResponseKeys.ReasoningNotAvailable] = true;
            }
        }
    }

    private static void AddCitations(IList<AIContent>? contents, List<CitationAnnotation> citations)
    {
        if (contents is not null && citations.Count > 0)
        {
            contents.Add(new TextContent(string.Empty) { Annotations = [.. citations] });
        }
    }

    // The raw model keeps fields the typed API does not know. JsonPatch is the only way to read them,
    // and it is marked experimental; the tests that read recorded responses guard against a change.
#pragma warning disable SCME0001
    private static List<CitationAnnotation> ReadCitations(ChatCompletion completion) =>
        ParseCitations(completion.Patch.TryGetJson("$.venice_parameters.web_search_citations"u8, out var json)
            ? json
            : default);

    private static List<CitationAnnotation> ReadCitations(StreamingChatCompletionUpdate chunk) =>
        ParseCitations(chunk.Patch.TryGetJson("$.venice_parameters.web_search_citations"u8, out var json)
            ? json
            : default);
#pragma warning restore SCME0001

    /// <summary>Citations are untrusted text. Only web addresses become links.</summary>
    private static List<CitationAnnotation> ParseCitations(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty)
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CitationAnnotation> citations = [];

        foreach (var citation in document.RootElement.EnumerateArray())
        {
            var address = citation.TryGetProperty("url", out var url) ? url.GetString() : null;

            if (Uri.TryCreate(address, UriKind.Absolute, out var link) && link.Scheme is "http" or "https")
            {
                var title = citation.TryGetProperty("title", out var name) ? name.GetString() : null;

                citations.Add(new CitationAnnotation { Title = title, Url = link });
            }
        }

        return citations;
    }

    #endregion

    #region Failure Mapping

    /// <summary>
    /// Finds the failure Drover understands anywhere in the exception chain. The vendor's own exception is not
    /// kept as the inner exception, so no vendor type leaves the adapter.
    /// </summary>
    internal static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var found = Describe(current);

            if (found is not null)
            {
                mapped = found;

                return true;
            }
        }

        mapped = null!;

        return false;
    }

    private static ModelProviderException? Describe(Exception exception) =>
        exception switch
        {
            ClientResultException failure => ModelProviderException.FromStatus(failure.Status, ReadWait(failure)),
            BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
            TimeoutRejectedException => ModelProviderException.FromTimeout(),
            HttpRequestException or HttpIOException => ModelProviderException.FromNetwork(exception),
            _ => null,
        };

    private static TimeSpan? ReadWait(ClientResultException failure) =>
        failure.GetRawResponse() is { } response
            ? VeniceHeaders.WaitUntilReset(
                name => response.Headers.TryGetValue(name, out var value) ? value : null,
                DateTimeOffset.UtcNow)
            : null;

    #endregion

    #region Vendor Types

    // The vendor client attaches its own objects to everything it returns. Nothing past this project may see them.
    private static void StripVendorTypes(ChatResponse response)
    {
        response.RawRepresentation = null;

        foreach (var message in response.Messages)
        {
            StripVendorTypes(message.Contents);
            message.RawRepresentation = null;
        }
    }

    private static void StripVendorTypes(ChatResponseUpdate update)
    {
        update.RawRepresentation = null;
        StripVendorTypes(update.Contents);
    }

    private static void StripVendorTypes(IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            content.RawRepresentation = null;

            foreach (var annotation in content.Annotations ?? [])
            {
                annotation.RawRepresentation = null;
            }
        }
    }

    #endregion
}
