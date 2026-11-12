namespace Drover.ModelTransport;

internal static class ModelChatMapping
{
    #region Requests

    public static ModelChatRequest ToModelRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        options ??= new ChatOptions();
        if (options.RawRepresentationFactory is not null || options.AdditionalProperties?.Count > 0
            || options.ConversationId is not null || options.ContinuationToken is not null
            || options.AllowBackgroundResponses == true || options.Reasoning?.Output is not null)
        {
            throw new NotSupportedException("Provider-specific chat options cannot cross the model boundary.");
        }

        return new([.. messages.Select(ToModelMessage)], new ModelChatOptions
        {
            ModelId = options.ModelId,
            Instructions = options.Instructions,
            MaxOutputTokens = options.MaxOutputTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            Seed = options.Seed,
            StopSequences = options.StopSequences?.ToArray(),
            Tools = options.Tools?.Select(ToModelTool).ToArray(),
            ToolMode = options.ToolMode switch
            {
                RequiredChatToolMode => "required",
                NoneChatToolMode => "none",
                _ => "auto",
            },
            RequiredToolName = (options.ToolMode as RequiredChatToolMode)?.RequiredFunctionName,
            AllowMultipleToolCalls = options.AllowMultipleToolCalls,
            ReasoningEffort = options.Reasoning?.Effort?.ToString(),
            ResponseFormat = options.ResponseFormat switch
            {
                ChatResponseFormatJson => "json",
                _ => null,
            },
            ResponseSchema = (options.ResponseFormat as ChatResponseFormatJson)?.Schema,
            ResponseSchemaName = (options.ResponseFormat as ChatResponseFormatJson)?.SchemaName,
            ResponseSchemaDescription = (options.ResponseFormat as ChatResponseFormatJson)?.SchemaDescription,
        });
    }

    public static ChatOptions ToChatOptions(ModelChatOptions options) => new()
    {
        ModelId = options.ModelId,
        Instructions = options.Instructions,
        MaxOutputTokens = options.MaxOutputTokens,
        Temperature = options.Temperature,
        TopP = options.TopP,
        TopK = options.TopK,
        FrequencyPenalty = options.FrequencyPenalty,
        PresencePenalty = options.PresencePenalty,
        Seed = options.Seed,
        StopSequences = options.StopSequences?.ToList(),
        Tools = options.Tools?.Select(tool => (AITool)new DeclarationOnlyFunction(tool)).ToList(),
        ToolMode = options.ToolMode switch
        {
            "required" => options.RequiredToolName is { } name ? ChatToolMode.RequireSpecific(name) : ChatToolMode.RequireAny,
            "none" => ChatToolMode.None,
            _ => ChatToolMode.Auto,
        },
        AllowMultipleToolCalls = options.AllowMultipleToolCalls,
        Reasoning = ToReasoningOptions(options.ReasoningEffort),
        ResponseFormat = options.ResponseSchema is { } schema
            ? ChatResponseFormat.ForJsonSchema(schema, options.ResponseSchemaName, options.ResponseSchemaDescription)
            : options.ResponseFormat == "json" ? ChatResponseFormat.Json : null,
    };

    public static ChatMessage ToChatMessage(ModelChatMessage message) => new(new ChatRole(message.Role), [.. message.Contents.Select(ToAIContent)])
    {
        MessageId = message.Id,
        AuthorName = message.AuthorName,
        CreatedAt = message.CreatedAt,
    };

    public static ModelChatMessage ToModelMessage(ChatMessage message) => new(message.Role.Value, [.. message.Contents.Select(ToModelContent)])
    {
        Id = message.MessageId,
        AuthorName = message.AuthorName,
        CreatedAt = message.CreatedAt,
    };

    private static ModelTool ToModelTool(AITool tool) => tool is AIFunctionDeclaration function
        ? new(function.Name, function.Description, function.JsonSchema.Clone())
        : throw new NotSupportedException("This model transport supports function tools only.");

    #endregion

    #region Responses

    public static ModelChatResponse ToModelResponse(ChatResponse response) => new([.. response.Messages.Select(ToModelMessage)])
    {
        Id = response.ResponseId,
        ModelId = response.ModelId,
        CreatedAt = response.CreatedAt,
        FinishReason = response.FinishReason?.Value,
        Usage = response.Usage is { } usage ? ToModelTokenUsage(usage) : null,
    };

    public static ChatResponse ToChatResponse(ModelChatResponse response) => new([.. response.Messages.Select(ToChatMessage)])
    {
        ResponseId = response.Id,
        ModelId = response.ModelId,
        CreatedAt = response.CreatedAt,
        FinishReason = response.FinishReason is { } reason ? new ChatFinishReason(reason) : null,
        Usage = response.Usage is { } usage ? ToUsageDetails(usage) : null,
        AdditionalProperties = response.Balances.Count > 0 ? new() { [ModelResponseKeys.Balances] = response.Balances } : null,
    };

    public static ModelChatUpdate ToModelUpdate(ChatResponseUpdate update) => new([.. update.Contents.Select(ToModelContent)])
    {
        ResponseId = update.ResponseId,
        MessageId = update.MessageId,
        ModelId = update.ModelId,
        Role = update.Role?.Value,
        AuthorName = update.AuthorName,
        CreatedAt = update.CreatedAt,
        FinishReason = update.FinishReason?.Value,
    };

    public static ChatResponseUpdate ToChatUpdate(ModelChatUpdate update) => new()
    {
        Contents = [.. update.Contents.Select(ToAIContent)],
        ResponseId = update.ResponseId,
        MessageId = update.MessageId,
        ModelId = update.ModelId,
        Role = update.Role is { } role ? new ChatRole(role) : null,
        AuthorName = update.AuthorName,
        CreatedAt = update.CreatedAt,
        FinishReason = update.FinishReason is { } reason ? new ChatFinishReason(reason) : null,
        AdditionalProperties = update.Balances.Count > 0 ? new() { [ModelResponseKeys.Balances] = update.Balances } : null,
    };

    #endregion

    #region Content

    private static ModelContent ToModelContent(AIContent content)
    {
        var result = content switch
        {
            TextContent text => (ModelContent)new ModelText(text.Text),
            TextReasoningContent reasoning => new ModelReasoning(reasoning.Text ?? string.Empty, ProtectedData: reasoning.ProtectedData),
            FunctionCallContent { Exception: not null } => throw new ModelProviderException(
                ModelFailure.Rejected, "The provider returned invalid tool arguments."),
            FunctionCallContent call => new ModelToolCall(call.CallId, call.Name, ToJson(call.Arguments)),
            FunctionResultContent resultContent => new ModelToolResult(resultContent.CallId, ToJson(resultContent.Result), resultContent.Exception is not null),
            DataContent data => new ModelData(data.Data.ToArray(), data.MediaType),
            UriContent uri => new ModelUri(uri.Uri, uri.MediaType),
            UsageContent usage => ToModelTokenUsage(usage.Details),
            _ => throw new NotSupportedException($"The model transport does not support {content.GetType().Name}."),
        };

        return result with
        {
            Citations = [.. (content.Annotations ?? []).Select(annotation => annotation is CitationAnnotation citation
                ? new ModelCitation(citation.Title, citation.Url)
                : throw new NotSupportedException("The model transport supports citation annotations only."))],
        };
    }

    private static AIContent ToAIContent(ModelContent content)
    {
        var result = content switch
        {
            ModelText text => (AIContent)new TextContent(text.Text),
            ModelReasoning reasoning => new TextReasoningContent(reasoning.Text)
            {
                ProtectedData = reasoning.ProtectedData,
                AdditionalProperties = reasoning.IsUnavailable ? new() { [ModelResponseKeys.ReasoningNotAvailable] = true } : null,
            },
            ModelToolCall call => new FunctionCallContent(call.CallId, call.Name, ToArguments(call.Arguments)),
            ModelToolResult resultContent => new FunctionResultContent(resultContent.CallId, ToResult(resultContent.Result)) { Exception = resultContent.HasError ? new InvalidOperationException("Tool execution failed.") : null },
            ModelData data => new DataContent(data.Data, data.MediaType),
            ModelUri uri => new UriContent(uri.Uri, uri.MediaType),
            ModelTokenUsage usage => new UsageContent(ToUsageDetails(usage)),
            _ => throw new NotSupportedException("This model content cannot be sent to the agent framework."),
        };

        result.Annotations = content.Citations.Count > 0
            ? [.. content.Citations.Select(citation => new CitationAnnotation { Title = citation.Title, Url = citation.Url })]
            : null;
        return result;
    }

    private static ModelTokenUsage ToModelTokenUsage(UsageDetails usage) => new(usage.InputTokenCount, usage.OutputTokenCount, usage.TotalTokenCount)
    {
        CachedInputTokens = usage.CachedInputTokenCount,
        ReasoningTokens = usage.ReasoningTokenCount,
        AdditionalCounts = usage.AdditionalCounts?.ToDictionary(pair => pair.Key, pair => pair.Value),
    };

    private static UsageDetails ToUsageDetails(ModelTokenUsage usage) => new()
    {
        InputTokenCount = usage.InputTokens,
        OutputTokenCount = usage.OutputTokens,
        TotalTokenCount = usage.TotalTokens,
        CachedInputTokenCount = usage.CachedInputTokens,
        ReasoningTokenCount = usage.ReasoningTokens,
        AdditionalCounts = usage.AdditionalCounts is { } counts ? [with(counts)] : null,
    };

    private static JsonElement ToJson(object? value) => value is JsonElement element ? element.Clone() : JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions);

    private static ReasoningOptions? ToReasoningOptions(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Equals("xhigh", StringComparison.OrdinalIgnoreCase))
        {
            return new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh };
        }

        return Enum.TryParse<ReasoningEffort>(value, true, out var effort) && Enum.IsDefined(effort)
            ? new ReasoningOptions { Effort = effort }
            : throw new NotSupportedException("This reasoning effort is not supported by the model transport.");
    }

    private static object? ToResult(JsonElement result) => result.ValueKind switch
    {
        JsonValueKind.String => result.GetString(),
        JsonValueKind.Null => null,
        _ => result.Clone(),
    };

    private static Dictionary<string, object?> ToArguments(JsonElement arguments) => arguments.ValueKind switch
    {
        JsonValueKind.Object => arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone()),
        JsonValueKind.Null => [],
        _ => throw new NotSupportedException("Tool arguments must be a JSON object or null for no arguments."),
    };

    private sealed class DeclarationOnlyFunction(ModelTool tool) : AIFunctionDeclaration
    {
        public override string Name => tool.Name;

        public override string Description => tool.Description;

        public override JsonElement JsonSchema => tool.Schema;
    }

    #endregion
}
