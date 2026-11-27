namespace Drover.Adapters.Gemini;

/// <summary>What one answer (or one streamed chunk) of the provider says, in Drover's own terms.</summary>
internal sealed record GeminiAnswer(
    IReadOnlyList<ModelContent> Contents,
    string? FinishReason,
    ModelTokenUsage? Usage,
    string? ResponseId,
    string? ModelId);

/// <summary>
/// Reads a <c>GenerateContentResponse</c>. A part that carries a <c>thoughtSignature</c> becomes a
/// <see cref="ModelReasoning"/> holding it in <see cref="ModelReasoning.ProtectedData"/>, placed directly before the
/// content of that part, so the request builder can put it back on the same part. A part marked <c>thought</c> is the
/// summary of the model's thinking and becomes reasoning text. The provider gives a function call no id on some
/// models, so a call without one gets an id made here, which is never sent back.
/// Sources (read 2026-11-27): https://ai.google.dev/api/generate-content (Candidate, <c>finishReason</c>,
/// <c>usageMetadata</c>, <c>promptFeedback</c>) and the thought signature guide named on <see cref="GeminiRequest"/>.
/// </summary>
internal static class GeminiReply
{
    #region Private Fields

    private const string MadeUpPrefix = "gemini-call-";

    #endregion

    #region Public Methods

    public static bool IsMadeUp(string callId) => callId.StartsWith(MadeUpPrefix, StringComparison.Ordinal);

    public static GeminiAnswer Read(JsonElement root)
    {
        var contents = new List<ModelContent>();
        string? finish = null;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    AddPart(contents, part);
                }
            }

            finish = candidate.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? FinishReason(reason.GetString()!)
                : null;
        }
        else if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
        {
            // The prompt itself was refused: no candidate exists, and the call ends as filtered.
            finish = "content_filter";
        }

        return new GeminiAnswer(contents, finish, Usage(root), Text(root, "responseId"), Text(root, "modelVersion"));
    }

    /// <summary>
    /// The provider's reasons for stopping. A natural stop, the limit, and the content filters are answers; a malformed
    /// call or a missing signature is a failure of the call, and says which.
    /// </summary>
    public static string FinishReason(string reason) => reason switch
    {
        "STOP" => "stop",
        "MAX_TOKENS" => "length",
        "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "LANGUAGE" => "content_filter",
        "MALFORMED_FUNCTION_CALL" => throw new ModelProviderException(
            ModelFailure.Rejected, "The model wrote a tool call the provider could not read (MALFORMED_FUNCTION_CALL). Try again."),
        "MISSING_THOUGHT_SIGNATURE" => throw new ModelProviderException(
            ModelFailure.Rejected, "The provider says a thought signature is missing from the conversation (MISSING_THOUGHT_SIGNATURE)."),
        _ => throw new ModelProviderException(ModelFailure.Rejected, $"The provider stopped the reply early ({reason})."),
    };

    #endregion

    #region Private Methods

    private static void AddPart(List<ModelContent> contents, JsonElement part)
    {
        var signature = Text(part, "thoughtSignature");
        var isThought = part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;
        var text = Text(part, "text");

        if (isThought)
        {
            contents.Add(new ModelReasoning(text ?? string.Empty, ProtectedData: signature));
            return;
        }

        if (signature is not null)
        {
            contents.Add(new ModelReasoning(string.Empty, ProtectedData: signature));
        }

        if (part.TryGetProperty("functionCall", out var call) && call.ValueKind == JsonValueKind.Object)
        {
            var id = Text(call, "id") ?? $"{MadeUpPrefix}{Guid.CreateVersion7():N}";
            var arguments = call.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object
                ? args.Clone()
                : JsonSerializer.SerializeToElement(new { });
            contents.Add(new ModelToolCall(id, Text(call, "name") ?? string.Empty, arguments));
        }
        else if (!string.IsNullOrEmpty(text))
        {
            contents.Add(new ModelText(text));
        }
    }

    private static ModelTokenUsage? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var thoughts = Count(usage, "thoughtsTokenCount");
        var output = Count(usage, "candidatesTokenCount");

        return new ModelTokenUsage(
            Count(usage, "promptTokenCount"),
            output is null && thoughts is null ? null : (output ?? 0) + (thoughts ?? 0),
            Count(usage, "totalTokenCount"))
        {
            CachedInputTokens = Count(usage, "cachedContentTokenCount"),
            ReasoningTokens = thoughts,
        };
    }

    private static long? Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count)
            ? count
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion
}
