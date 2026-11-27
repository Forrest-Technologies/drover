namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Reads what a server's model list says about a model beyond its id, by the shape of the entry. No host name is
/// checked: an entry that carries a field is read, and one that does not is read as a bare id.
/// </summary>
internal static class PublishedModelFacts
{
    #region Private Fields

    private const decimal PerMillion = 1_000_000m;

    #endregion

    #region Public Methods

    /// <summary>The model an entry of the list describes, or null when the entry has no id.</summary>
    public static ModelInfo? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idElement) || idElement.GetString() is not { } id)
        {
            return null;
        }

        var facts = ReadRoutedFacts(entry, KnownVoices.Mark(new ModelCapabilities(), id));
        facts = ReadServedContext(entry, facts);

        return new ModelInfo(id, id, facts, []) { Pricing = ReadPrices(entry) };
    }

    #endregion

    #region Routed Lists

    /// <summary>
    /// A routing service's entry: https://openrouter.ai/docs/api-reference/models/get-models. A list of supported
    /// parameters is a statement about the model, so a model that leaves out <c>tools</c> is read as not calling
    /// them.
    /// </summary>
    private static ModelCapabilities ReadRoutedFacts(JsonElement entry, ModelCapabilities facts)
    {
        facts = facts with
        {
            ContextTokens = ReadWholeNumber(entry, "context_length") ?? facts.ContextTokens,
            MaxOutputTokens = entry.TryGetProperty("top_provider", out var provider) ? ReadWholeNumber(provider, "max_completion_tokens") : null,
        };

        if (entry.TryGetProperty("architecture", out var architecture) && ReadStrings(architecture, "input_modalities") is { } inputs)
        {
            facts = facts with { Vision = inputs.Contains("image") };
        }

        return ReadSupportedParameters(entry, facts);
    }

    private static ModelCapabilities ReadSupportedParameters(JsonElement entry, ModelCapabilities facts) =>
        ReadStrings(entry, "supported_parameters") is { } parameters
            ? facts with
            {
                ToolCalling = parameters.Contains("tools"),
                Reasoning = parameters.Contains("reasoning"),
                ReasoningEfforts = parameters.Contains("reasoning") ? ReasoningLevels.Documented : facts.ReasoningEfforts,
                StructuredOutput = parameters.Contains("structured_outputs"),
            }
            : facts;

    #endregion

    #region Served Context

    /// <summary>
    /// A server that serves one model says how long its context is on the plain list entry as <c>max_model_len</c>:
    /// https://github.com/vllm-project/vllm/blob/main/vllm/entrypoints/openai/protocol.py (<c>ModelCard</c>). The
    /// other names some servers use (<c>context_window</c>, <c>max_context_length</c>) are not confirmed against a
    /// server's documentation, so they are not read.
    /// </summary>
    private static ModelCapabilities ReadServedContext(JsonElement entry, ModelCapabilities facts) =>
        facts.ContextTokens is null && ReadWholeNumber(entry, "max_model_len") is { } length
            ? facts with { ContextTokens = length }
            : facts;

    #endregion

    #region Prices

    /// <summary>
    /// Prices are strings of US dollars per token. A negative one (a routing service writes -1 for a price that
    /// varies) or one that does not parse stays unknown.
    /// </summary>
    private static ModelPricing ReadPrices(JsonElement entry)
    {
        if (!entry.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Object)
        {
            return new ModelPricing();
        }

        return new ModelPricing
        {
            InputPerMillion = ReadPrice(pricing, "prompt"),
            OutputPerMillion = ReadPrice(pricing, "completion"),
            CachedInputPerMillion = ReadPrice(pricing, "input_cache_read"),
        };
    }

    private static decimal? ReadPrice(JsonElement pricing, string name)
    {
        if (!pricing.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var parsed = decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken)
            ? perToken
            : (decimal?)null;

        return parsed is >= 0 ? decimal.Round(parsed.Value * PerMillion, 6) : null;
    }

    #endregion

    #region Private Methods

    private static int? ReadWholeNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
        && number > 0
            ? number
            : null;

    private static HashSet<string>? ReadStrings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var list)
        && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : null;

    #endregion
}
