namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The provider's Responses API (<c>POST {address}/responses</c>) through the vendor's chat client, exposing only
/// Drover-owned transport values. Drover keeps the conversation, so every request says <c>store: false</c> and none
/// names an earlier response: the whole history goes in <c>input</c> each time. Reasoning items come back with
/// <c>encrypted_content</c>, which Drover carries in <see cref="ModelReasoning.ProtectedData"/> and sends back
/// unchanged, so the model keeps its reasoning across a tool call.
/// Sources (read 2026-11-26): https://developers.openai.com/api/reference/resources/responses/methods/create,
/// https://developers.openai.com/api/docs/guides/reasoning ("Preserve reasoning without stored responses" and
/// "Reasoning summaries"), https://developers.openai.com/api/docs/guides/function-calling,
/// https://developers.openai.com/api/docs/guides/streaming-responses.
/// </summary>
internal sealed class ResponsesChatClient(
    HttpClient httpClient,
    ResponsesDestination destination,
    string modelId,
    ModelSettings settings) : IModelChatClient
{
    #region Private Fields

    /// <summary>What the vendor client calls a refusal content part when it hands it over as an error.</summary>
    private const string RefusalCode = "Refusal";

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        RefuseFilesOtherThanPictures(request);
        var headers = new ProtocolHeaderCapture();
        using var client = CreateClient(headers);
        ChatResponse response;

        try
        {
            response = await client.GetResponseAsync(ToChatMessages(request), ToChatOptions(request.Options), cancellation);
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw ProtocolChatClient.WithHeaders(mapped, headers.Headers);
        }

        var refused = false;

        foreach (var message in response.Messages)
        {
            refused |= ReplaceRefusals(message.Contents, headers.Headers);
        }

        if (refused)
        {
            response.FinishReason = ChatFinishReason.ContentFilter;
        }

        return ModelChatMapping.ToModelResponse(response) with { Headers = headers.Headers };
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        RefuseFilesOtherThanPictures(request);
        var headers = new ProtocolHeaderCapture();
        using var client = CreateClient(headers);
        await using var updates = client.GetStreamingResponseAsync(ToChatMessages(request), ToChatOptions(request.Options), cancellation).GetAsyncEnumerator(cancellation);
        var refused = false;

        while (true)
        {
            var hasNext = false;

            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
            {
                throw ProtocolChatClient.WithHeaders(mapped, headers.Headers);
            }

            if (!hasNext)
            {
                yield break;
            }

            var update = updates.Current;
            SettleEnding(update, headers.Headers);
            refused |= ReplaceRefusals(update.Contents, headers.Headers);

            if (refused && update.FinishReason is not null)
            {
                update.FinishReason = ChatFinishReason.ContentFilter;
            }

            yield return ModelChatMapping.ToModelUpdate(update) with { Headers = headers.Headers };
        }
    }

    public void Dispose() => httpClient.Dispose();

    #endregion

    #region Request Shaping

    /// <summary>
    /// Reasoning goes back only as the provider sealed it. A summary with no <c>encrypted_content</c> (the words a
    /// stream sends ahead of the sealed item, or reasoning kept as text in a thread's history) is words for people,
    /// and the provider has no stored item to match it to when <c>store</c> is false, so it is left out.
    /// </summary>
    private static IEnumerable<ChatMessage> ToChatMessages(ModelChatRequest request) =>
        request.Messages
            .Select(message => message with { Contents = [.. message.Contents.Where(content => content is not ModelReasoning { ProtectedData: null })] })
            .Where(message => message.Contents.Count > 0)
            .Select(ModelChatMapping.ToChatMessage);

    private ChatOptions ToChatOptions(ModelChatOptions options)
    {
        var effective = ProtocolChatClient.WithDefaults(options, settings);
        var effort = effective.ReasoningEffort?.ToLowerInvariant();
        var mapped = ModelChatMapping.ToChatOptions(effective with { ReasoningEffort = null });
        mapped.RawRepresentationFactory = _ => ShapeRequest(effort);
        return mapped;
    }

    /// <summary>
    /// What the vendor client cannot be told through the shared options. <c>store</c> is always false: the provider
    /// keeps nothing. A reasoning summary is always asked for, so <see cref="ModelReasoning.Text"/> has words; a model
    /// that does not reason accepts the summary setting and ignores it. The effort goes as the bot set it, any value the
    /// provider documents, because the accepted values differ by model and the provider's own refusal is clearer than
    /// one written here. <c>include</c> is not sent: a request with <c>store: false</c> gets
    /// <c>encrypted_content</c> by default, and a model that does not reason refuses the include.
    /// </summary>
#pragma warning disable OPENAI001 // The vendor marks its Responses types experimental; this adapter is where they belong.
    private static OpenAI.Responses.CreateResponseOptions ShapeRequest(string? effort)
    {
        var reasoning = new OpenAI.Responses.ResponseReasoningOptions
        {
            ReasoningSummaryVerbosity = OpenAI.Responses.ResponseReasoningSummaryVerbosity.Auto,
        };

        if (effort is not null)
        {
            reasoning.ReasoningEffortLevel = new OpenAI.Responses.ResponseReasoningEffortLevel(effort);
        }

        return new() { StoredOutputEnabled = false, ReasoningOptions = reasoning };
    }

    private IChatClient CreateClient(ProtocolHeaderCapture capture)
    {
        var options = ProtocolChatClient.CreateOptions(httpClient, destination.Address);
        options.AddPolicy(capture, PipelinePosition.PerCall);

        if (destination.Headers.Count > 0)
        {
            options.AddPolicy(new ProtocolRequestHeaders(destination.Headers), PipelinePosition.PerCall);
        }

        return new OpenAIClient(new ApiKeyCredential(destination.Credential), options).GetResponsesClient().AsIChatClient(modelId);
    }

#pragma warning restore OPENAI001

    /// <summary>Pictures go as image input parts. A model reads other files as text, which the engine sends.</summary>
    private static void RefuseFilesOtherThanPictures(ModelChatRequest request)
    {
        var other = request.Messages
            .SelectMany(message => message.Contents)
            .Any(content => content switch
            {
                ModelData data => !IsPicture(data.MediaType),
                ModelUri uri => !IsPicture(uri.MediaType),
                _ => false,
            });

        if (other)
        {
            throw ModelProviderException.FromRefusal("This provider reads pictures, but not other files. Send the file's text instead.");
        }
    }

    private static bool IsPicture(string mediaType) => mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Reply Settling

    /// <summary>
    /// The vendor client hands a refusal part to Drover as an error, and other errors the stream reports the same way.
    /// A refusal is the model's answer: its words become the reply, and the call ends as filtered. Any other error is
    /// a failure of the call. Returns whether a refusal was found.
    /// </summary>
    private static bool ReplaceRefusals(IList<AIContent> contents, IReadOnlyDictionary<string, string> headers)
    {
        var refused = false;

        for (var index = 0; index < contents.Count; index++)
        {
            if (contents[index] is not ErrorContent error)
            {
                continue;
            }

            if (error.ErrorCode != RefusalCode)
            {
                throw ProtocolChatClient.WithHeaders(FailureFor(error.ErrorCode), headers);
            }

            contents[index] = new TextContent(error.Message);
            refused = true;
        }

        return refused;
    }

    /// <summary>
    /// A stream ends with <c>response.completed</c>, <c>response.incomplete</c> or <c>response.failed</c>. The vendor
    /// client passes the last two on with nothing in them, so they are read here: a cut answer ends as "length" (or as
    /// filtered when the provider says so), and a failed one is a failure of the call.
    /// </summary>
#pragma warning disable OPENAI001 // The vendor marks its Responses types experimental; this adapter is where they belong.
    private static void SettleEnding(ChatResponseUpdate update, IReadOnlyDictionary<string, string> headers)
    {
        switch (update.RawRepresentation)
        {
            case OpenAI.Responses.StreamingResponseFailedUpdate failed:
                throw ProtocolChatClient.WithHeaders(FailureFor(failed.Response?.Error?.Code.ToString()), headers);
            case OpenAI.Responses.StreamingResponseIncompleteUpdate incomplete:
                update.FinishReason = incomplete.Response?.IncompleteStatusDetails?.Reason == OpenAI.Responses.ResponseIncompleteStatusReason.ContentFilter
                    ? ChatFinishReason.ContentFilter
                    : ChatFinishReason.Length;
                break;
        }
    }

#pragma warning restore OPENAI001

    /// <summary>
    /// Error codes a response or a stream reports in its body. The provider's message is not repeated: it can quote
    /// the person's input. Codes from https://developers.openai.com/api/docs/guides/error-codes and the response
    /// object's <c>error.code</c> in the create reference.
    /// </summary>
    internal static ModelProviderException FailureFor(string? code) =>
        code?.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "ratelimitexceeded" => ModelProviderException.FromStatus((int)HttpStatusCode.TooManyRequests),
            "insufficientquota" => ModelProviderException.FromStatus((int)HttpStatusCode.PaymentRequired),
            "contextlengthexceeded" => new(ModelFailure.TooLong, "The provider says this conversation is longer than the model can read."),
            "servererror" => new(ModelFailure.Unreachable, "The provider failed on its side while answering. Try again shortly."),
            _ => new(ModelFailure.Rejected, "The provider stopped before the reply was finished."),
        };

    #endregion
}
