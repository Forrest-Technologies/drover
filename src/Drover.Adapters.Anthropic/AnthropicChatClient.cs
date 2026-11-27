namespace Drover.Adapters.Anthropic;

/// <summary>
/// The provider's Messages API (<c>POST /v1/messages</c>) through the vendor's chat client, exposing only Drover-owned
/// transport values. What a request carries depends on the model, so the client reads the model's own entry
/// (<c>GET /v1/models/{id}</c>) once, before its first call: the longest answer it may give, and whether it takes
/// adaptive thinking.
/// Sources (read 2026-11-27): https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview (every tool result
/// of a turn in one user message, results first), https://platform.claude.com/docs/en/build-with-claude/adaptive-thinking
/// and https://platform.claude.com/docs/en/build-with-claude/thinking-steering-and-cost (<c>thinking: {type:
/// "adaptive", display: "summarized"}</c>, depth by <c>output_config.effort</c>), https://platform.claude.com/docs/en/api/errors
/// ("Thinking blocks cannot be modified": every thinking and redacted thinking block goes back as it came, empty ones
/// too), https://platform.claude.com/docs/en/build-with-claude/prompt-caching (block breakpoints and the top-level
/// <c>cache_control</c>, at most four in all), https://platform.claude.com/docs/en/build-with-claude/pdf-support
/// (a <c>document</c> block with a base64 source) and https://platform.claude.com/docs/en/api/versioning (on
/// <c>2026-10-21</c> a request that sends <c>temperature</c>, <c>top_p</c> or <c>top_k</c> is refused on every model).
/// </summary>
internal sealed class AnthropicChatClient(
    IHttpClientFactory httpClients,
    string httpClientName,
    AnthropicDestination destination,
    DiscoveryLimits limits,
    string modelId,
    ModelSettings settings) : IModelChatClient
{
    #region Private Fields

    /// <summary>The key the vendor client gives the tokens written to the cache, and the one Drover reports them under.</summary>
    private const string VendorCacheWrites = "CacheCreationInputTokens";

    public const string CacheWriteTokens = "cache_write_tokens";

    /// <summary>What each model's own entry said, read once per client: a run makes many calls to one model.</summary>
    private readonly Dictionary<string, ModelInfo> models = [with(StringComparer.Ordinal)];

    private readonly SemaphoreSlim reading = new(1, 1);

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        var (messages, options) = await Shape(request, cancellation);
        var capture = new AnthropicHeaderCapture();
        using var client = CreateClient(capture);

        try
        {
            var response = await client.AsIChatClient(modelId).GetResponseAsync(messages, options, cancellation);
            return ToModelResponse(response) with { Headers = capture.Headers };
        }
        catch (Exception exception) when (AnthropicFailures.TryMap(exception, out var mapped, capture.RetryAfter))
        {
            throw WithHeaders(mapped, capture.Headers);
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var (messages, options) = await Shape(request, cancellation);
        var capture = new AnthropicHeaderCapture();
        using var client = CreateClient(capture);
        await using var updates = client.AsIChatClient(modelId).GetStreamingResponseAsync(messages, options, cancellation).GetAsyncEnumerator(cancellation);

        while (true)
        {
            var hasNext = false;

            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception) when (AnthropicFailures.TryMap(exception, out var mapped, capture.RetryAfter))
            {
                throw WithHeaders(mapped, capture.Headers);
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return ToModelUpdate(updates.Current) with { Headers = capture.Headers };
        }
    }

    public void Dispose() => reading.Dispose();

    #endregion

    #region Request Shaping

    /// <summary>Refuses in words what this provider cannot take, then builds the request for the model the call names.</summary>
    private async Task<(List<ChatMessage> Messages, ChatOptions Options)> Shape(ModelChatRequest request, CancellationToken cancellation)
    {
        var effective = WithDefaults(request.Options);
        RefuseWhatTheProviderCannotTake(request, effective);
        var model = effective.ModelId ?? modelId;
        var facts = await Facts(model, cancellation);
        var instructions = SystemBlocks(effective.Instructions);
        return ([.. instructions, .. ToChatMessages(request.Messages)], ToChatOptions(effective, model, facts, cacheAfterTools: instructions.Count == 0));
    }

    /// <summary>The bot's settings fill what the call leaves unset. Sampling settings are not carried at all.</summary>
    private ModelChatOptions WithDefaults(ModelChatOptions options) => options with
    {
        MaxOutputTokens = options.MaxOutputTokens ?? settings.MaxOutputTokens,
        ReasoningEffort = options.ReasoningEffort ?? settings.ReasoningEffort,
        ResponseFormat = options.ResponseFormat ?? (settings.StructuredOutput ? "json" : null),
    };

    /// <summary>
    /// The provider's newest models refuse a forced tool choice whatever the thinking setting, and older ones refuse it
    /// while thinking, so a call that asks for one is refused here in words. A JSON answer is given only to a schema,
    /// and a model is given pictures and PDFs, nothing else; the engine gives other files as text.
    /// </summary>
    private static void RefuseWhatTheProviderCannotTake(ModelChatRequest request, ModelChatOptions options)
    {
        if (options.ToolMode is "required" || options.RequiredToolName is not null)
        {
            throw ModelProviderException.FromRefusal("This provider does not take a forced tool choice. The model decides when to call a tool.");
        }

        if (options.ResponseFormat is "json" && options.ResponseSchema is null)
        {
            throw ModelProviderException.FromRefusal("This provider answers in JSON only to a schema. Turn off the JSON-only reply for this bot.");
        }

        var other = request.Messages
            .SelectMany(message => message.Contents)
            .Any(content => content switch
            {
                ModelData data => !IsReadable(data.MediaType),
                ModelUri uri => !IsReadable(uri.MediaType),
                _ => false,
            });

        if (other)
        {
            throw ModelProviderException.FromRefusal("This provider reads pictures and PDFs, but not other files. Send the file's text instead.");
        }
    }

    private static bool IsReadable(string mediaType) =>
        mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The longest answer comes from the model's own entry when the bot sets none, and a bot's setting only lowers it.
    /// A model that takes adaptive thinking is asked for it with summaries, so the reasoning has words; the effort goes
    /// as the bot set it, because the levels each model takes differ and its own refusal says which. The request has one
    /// cache breakpoint after the instructions, or after the tools where there are no instructions, and the automatic
    /// one at the end of the conversation.
    /// </summary>
    private static ChatOptions ToChatOptions(ModelChatOptions effective, string model, ModelInfo facts, bool cacheAfterTools)
    {
        var maxTokens = MaxTokens(effective.MaxOutputTokens, facts.Capabilities.MaxOutputTokens);
        var effort = effective.ReasoningEffort?.ToLowerInvariant();
        var mapped = ModelChatMapping.ToChatOptions(effective with
        {
            ModelId = model,
            Instructions = null,
            MaxOutputTokens = maxTokens,
            Temperature = null,
            TopP = null,
            TopK = null,
            FrequencyPenalty = null,
            PresencePenalty = null,
            Seed = null,
            ReasoningEffort = null,
            Tools = null,
        });
        var tools = effective.Tools ?? [];
        mapped.Tools = tools.Count > 0
            ? [.. tools.Select((tool, index) => (AITool)new Declaration(tool, cacheAfterTools && index == tools.Count - 1))]
            : null;
        mapped.ToolMode = tools.Count > 0 ? mapped.ToolMode : null;
        mapped.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [],
            CacheControl = new CacheControlEphemeral(),
            Thinking = facts.Capabilities.Reasoning ? (ThinkingConfigParam)new ThinkingConfigAdaptive { Display = Display.Summarized } : null,
            OutputConfig = effort is null ? null : new OutputConfig { Effort = effort },
        };
        return mapped;
    }

    /// <summary>
    /// The provider needs a length on every request, so a model that names none needs one from the bot. A bot's own
    /// turns stream; the screen reader and the summary set their own small limit. A delegated child run can be a whole
    /// call with the model's longest answer: the provider takes it, the package's own length check for whole calls
    /// does not run because this client sets the timeout, and the named client's attempt timeout bounds the wait
    /// (call timeouts are #437).
    /// </summary>
    private static int MaxTokens(int? asked, int? modelLimit) => (asked, modelLimit) switch
    {
        ({ } bot, { } limit) => Math.Min(bot, limit),
        ({ } bot, null) => bot,
        (null, { } limit) => limit,
        _ => throw ModelProviderException.FromRefusal(
            "The provider does not say how long this model's answers may be. Set an output limit on the bot."),
    };

    /// <summary>
    /// The system blocks: a destination's leading block, then the instructions, with the cache breakpoint on the last,
    /// so the tools and the system text are cached as one prefix.
    /// </summary>
    private List<ChatMessage> SystemBlocks(string? instructions)
    {
        List<AIContent> blocks = [];

        if (destination.SystemPrefix is { Length: > 0 } prefix)
        {
            blocks.Add(new TextContent(prefix));
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            blocks.Add(new TextContent(instructions));
        }

        if (blocks.Count == 0)
        {
            return [];
        }

        blocks[^1].WithCacheControl(new CacheControlEphemeral());
        return [new ChatMessage(ChatRole.System, blocks)];
    }

    /// <summary>
    /// Drover's messages as the vendor client takes them. A tool's results and the person's words that follow them are
    /// one user message, results first. An assistant's reasoning goes back as the blocks the provider gave: the pieces of
    /// one streamed block are joined up to its signature, a redacted block goes back as redacted, and reasoning with no
    /// signature (words kept in a thread's history) is left out, because the provider refuses an unsigned block.
    /// </summary>
    private static IEnumerable<ChatMessage> ToChatMessages(IReadOnlyList<ModelChatMessage> messages)
    {
        List<ModelChatMessage> joined = [];

        foreach (var message in messages)
        {
            var contents = message.Role == "assistant" ? ThinkingBlocks(message.Contents) : [.. message.Contents];
            var role = message.Role is "tool" ? "user" : message.Role;

            if (contents.Count == 0)
            {
                continue;
            }

            if (role == "user" && joined is [.., { Role: "user" } last])
            {
                joined[^1] = last with { Contents = [.. last.Contents, .. contents] };
            }
            else
            {
                joined.Add(message with { Role = role, Contents = contents });
            }
        }

        return joined.Select(ModelChatMapping.ToChatMessage).Select(MarkRedactedThinking);
    }

    private static List<ModelContent> ThinkingBlocks(IReadOnlyList<ModelContent> contents)
    {
        List<ModelContent> blocks = [];
        var pending = new StringBuilder();

        foreach (var content in contents)
        {
            if (content is not ModelReasoning reasoning)
            {
                pending.Clear();
                blocks.Add(content);
                continue;
            }

            if (reasoning.IsUnavailable)
            {
                pending.Clear();

                if (reasoning.ProtectedData is not null)
                {
                    blocks.Add(reasoning);
                }

                continue;
            }

            pending.Append(reasoning.Text);

            if (reasoning.ProtectedData is not null)
            {
                blocks.Add(new ModelReasoning(pending.ToString(), ProtectedData: reasoning.ProtectedData));
                pending.Clear();
            }
        }

        return blocks;
    }

    private static ChatMessage MarkRedactedThinking(ChatMessage message)
    {
        foreach (var content in message.Contents)
        {
            if (content is TextReasoningContent { ProtectedData: { } data } reasoning
                && reasoning.AdditionalProperties?.ContainsKey(ModelResponseKeys.ReasoningNotAvailable) == true)
            {
                reasoning.RawRepresentation = (ContentBlockParam)new RedactedThinkingBlockParam { Data = data };
            }
        }

        return message;
    }

    private async Task<ModelInfo> Facts(string model, CancellationToken cancellation)
    {
        await reading.WaitAsync(cancellation);

        try
        {
            if (!models.TryGetValue(model, out var facts))
            {
                using var client = httpClients.CreateClient(httpClientName);
                facts = await AnthropicModels.Find(client, destination, model, limits, cancellation);
                models[model] = facts;
            }

            return facts;
        }
        finally
        {
            reading.Release();
        }
    }

    private AnthropicClient CreateClient(AnthropicHeaderCapture capture) => new(new ClientOptions
    {
        HttpClient = httpClients.CreateClient(httpClientName),
        BaseUrl = destination.Address.AbsoluteUri.TrimEnd('/'),
        ApiKey = destination.Credential,
        ExtraHeaders = destination.Headers.Count > 0 ? destination.Headers : null,
        MaxRetries = 0,
        Timeout = Timeout.InfiniteTimeSpan,
        OpenTelemetry = false,
        Handlers = [capture],
    });

    #endregion

    #region Reply Mapping

    private static ModelChatResponse ToModelResponse(ChatResponse response)
    {
        foreach (var message in response.Messages)
        {
            KeepWhatDroverCarries(message.Contents);
        }

        RenameCacheWrites(response.Usage);
        return ModelChatMapping.ToModelResponse(response);
    }

    private static ModelChatUpdate ToModelUpdate(ChatResponseUpdate update)
    {
        KeepWhatDroverCarries(update.Contents);
        return ModelChatMapping.ToModelUpdate(update);
    }

    /// <summary>
    /// Text, reasoning, tool calls and usage cross the boundary. A redacted thinking block is reasoning whose words the
    /// provider withheld, so it is marked as such and goes back as redacted. A streamed thinking block opens with an
    /// empty signature and gets its real one at its end, so the empty one is not a signature. Anything else the vendor client can hand
    /// over (the blocks of provider-hosted tools, which this adapter never asks for) is dropped on purpose.
    /// </summary>
    private static void KeepWhatDroverCarries(IList<AIContent> contents)
    {
        for (var index = contents.Count - 1; index >= 0; index--)
        {
            switch (contents[index])
            {
                case TextReasoningContent { RawRepresentation: RedactedThinkingBlock } redacted:
                    (redacted.AdditionalProperties ??= [])[ModelResponseKeys.ReasoningNotAvailable] = true;
                    break;
                case TextReasoningContent { ProtectedData: "" } opening:
                    opening.ProtectedData = null;
                    break;
                case UsageContent usage:
                    RenameCacheWrites(usage.Details);
                    break;
                case TextContent or TextReasoningContent or FunctionCallContent:
                    break;
                default:
                    contents.RemoveAt(index);
                    break;
            }
        }
    }

    /// <summary>
    /// The vendor client already reports usage in Drover's meaning: input is every input token, cache reads and writes
    /// included, and the cache reads are the cached input. The cache writes go under Drover's own name.
    /// </summary>
    private static void RenameCacheWrites(UsageDetails? usage)
    {
        if (usage?.AdditionalCounts is { } counts && counts.TryGetValue(VendorCacheWrites, out var writes))
        {
            counts.Remove(VendorCacheWrites);
            counts[CacheWriteTokens] = writes;
        }
    }

    private static ModelProviderException WithHeaders(ModelProviderException failure, IReadOnlyDictionary<string, string> headers) =>
        new(failure.Failure, failure.Message, failure.RetryAfter) { ResponseHeaders = headers };

    #endregion

    #region Tools

    /// <summary>A tool as the vendor client takes it, with the cache breakpoint after the last one when asked.</summary>
    private sealed class Declaration(ModelTool tool, bool cacheAfter) : AIFunctionDeclaration
    {
        #region Properties

        public override string Name => tool.Name;

        public override string Description => tool.Description;

        public override JsonElement JsonSchema => tool.Schema;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; } = cacheAfter
            ? new Dictionary<string, object?> { [nameof(Tool.CacheControl)] = new CacheControlEphemeral() }
            : EmptyProperties;

        private static Dictionary<string, object?> EmptyProperties => [];

        #endregion
    }

    #endregion
}
