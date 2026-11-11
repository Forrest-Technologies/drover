namespace Drover.Adapters.Venice;

/// <summary>Reads Venice's public model list and trait list into Drover's capability record.</summary>
internal static class VeniceModelCatalog
{
    #region Public Methods

    /// <summary>Models Venice marks offline are left out. Prices are dollars per million tokens.</summary>
    public static List<ModelInfo> Parse(JsonElement models, JsonElement? traits)
    {
        var traitsByModel = TraitNamesByModel(traits);

        if (!models.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. data.EnumerateArray()
                .Where(model => !IsOffline(model))
                .Select(model => ToModelInfo(model, traitsByModel)),
        ];
    }

    #endregion

    #region Private Methods

    private static ModelInfo ToModelInfo(JsonElement model, Dictionary<string, List<string>> traitsByModel)
    {
        var id = model.GetProperty("id").GetString()!;
        var spec = model.GetProperty("model_spec");
        var capabilities = spec.TryGetProperty("capabilities", out var found) ? found : default;
        var ownTraits = StringArray(spec, "traits");
        var traits = traitsByModel.TryGetValue(id, out var named) ? [.. ownTraits.Union(named)] : ownTraits;

        return new ModelInfo(id, ReadString(spec, "name") ?? id, ToCapabilities(model, spec, capabilities), traits);
    }

    private static ModelCapabilities ToCapabilities(JsonElement model, JsonElement spec, JsonElement capabilities) =>
        new()
        {
            ContextTokens = ReadInt(spec, "availableContextTokens") ?? ReadInt(model, "context_length"),
            MaxOutputTokens = ReadInt(spec, "maxCompletionTokens"),
            ToolCalling = Flag(capabilities, "supportsFunctionCalling"),
            Vision = Flag(capabilities, "supportsVision"),
            Reasoning = Flag(capabilities, "supportsReasoning"),
            ReasoningEfforts = Flag(capabilities, "supportsReasoningEffort")
                ? StringArray(capabilities, "reasoningEffortOptions")
                : [],
            StructuredOutput = Flag(capabilities, "supportsResponseSchema"),
            WebSearch = Flag(capabilities, "supportsWebSearch"),
            InputPricePerMillion = ReadPrice(spec, "input"),
            OutputPricePerMillion = ReadPrice(spec, "output"),
            Privacy = ReadPrivacy(ReadString(spec, "privacy"), Flag(capabilities, "supportsTeeAttestation")),
        };

    private static PrivacyMode ReadPrivacy(string? privacy, bool attested) =>
        (privacy, attested) switch
        {
            (_, true) => PrivacyMode.Attested,
            ("private", _) => PrivacyMode.Private,
            ("anonymized", _) => PrivacyMode.Anonymized,
            _ => PrivacyMode.Unknown,
        };

    private static Dictionary<string, List<string>> TraitNamesByModel(JsonElement? traits)
    {
        Dictionary<string, List<string>> byModel = [];

        if (traits is not { } root || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return byModel;
        }

        foreach (var trait in data.EnumerateObject().Where(trait => trait.Value.ValueKind == JsonValueKind.String))
        {
            var modelId = trait.Value.GetString()!;

            if (!byModel.TryGetValue(modelId, out var names))
            {
                byModel[modelId] = names = [];
            }

            names.Add(trait.Name);
        }

        return byModel;
    }

    private static bool IsOffline(JsonElement model) =>
        model.TryGetProperty("model_spec", out var spec) && Flag(spec, "offline");

    private static decimal? ReadPrice(JsonElement spec, string direction) =>
        spec.TryGetProperty("pricing", out var pricing)
        && pricing.TryGetProperty(direction, out var price)
        && price.TryGetProperty("usd", out var usd)
        && usd.ValueKind == JsonValueKind.Number
            ? usd.GetDecimal()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] StringArray(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    #endregion
}
