namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Reads Copilot's model list into Drover's own shape. Models that cannot call tools, or that the account has
/// disabled, are dropped. When the picker list is empty on the individual endpoint, models whose policy is
/// enabled are kept, because some individual accounts leave every picker flag false.
/// </summary>
internal static class CopilotCatalog
{
    #region Public Methods

    public static IReadOnlyList<ModelInfo> Parse(JsonElement root, bool allowPolicyFallback)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind is not JsonValueKind.Array)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        List<Candidate> candidates = [];

        foreach (var item in data.EnumerateArray())
        {
            if (Read(item) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        var picked = candidates.Where(candidate => candidate.PickerEnabled && candidate.PolicyState != "disabled").ToList();

        if (picked.Count == 0 && allowPolicyFallback)
        {
            picked = [.. candidates.Where(candidate => candidate.PolicyState == "enabled")];
        }

        return [.. picked.Select(candidate => candidate.Info)];
    }

    #endregion

    #region Private Methods

    private static Candidate? Read(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id)
        {
            return null;
        }

        var capabilities = item.TryGetProperty("capabilities", out var capabilitiesElement) ? capabilitiesElement : default;
        var supports = capabilities.ValueKind is JsonValueKind.Object && capabilities.TryGetProperty("supports", out var supportsElement)
            ? supportsElement
            : default;

        if (supports.ValueKind is JsonValueKind.Object
            && supports.TryGetProperty("tool_calls", out var tools)
            && tools.ValueKind is JsonValueKind.False)
        {
            return null;
        }

        var picker = item.TryGetProperty("model_picker_enabled", out var pickerElement) && pickerElement.ValueKind is JsonValueKind.True;
        var policy = item.TryGetProperty("policy", out var policyElement)
            && policyElement.ValueKind is JsonValueKind.Object
            && policyElement.TryGetProperty("state", out var state)
            ? state.GetString()
            : null;
        var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var vision = supports.ValueKind is JsonValueKind.Object
            && supports.TryGetProperty("vision", out var visionElement)
            && visionElement.ValueKind is JsonValueKind.True;
        var reasoning = supports.ValueKind is JsonValueKind.Object
            && (Flag(supports, "thinking") || Flag(supports, "reasoning"));
        var context = ReadLimit(capabilities, "max_context_window") ?? ReadLimit(capabilities, "max_prompt_tokens");
        var output = ReadLimit(capabilities, "max_output_tokens") ?? ReadLimit(capabilities, "max_completion_tokens");

        return new Candidate(
            picker,
            policy,
            new ModelInfo(
                id,
                string.IsNullOrWhiteSpace(name) ? id : name,
                new ModelCapabilities
                {
                    ContextTokens = context,
                    MaxOutputTokens = output,
                    ToolCalling = true,
                    Streaming = true,
                    Vision = vision,
                    Reasoning = reasoning,
                },
                []));
    }

    private static bool Flag(JsonElement supports, string name) =>
        supports.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static int? ReadLimit(JsonElement capabilities, string name)
    {
        if (capabilities.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (capabilities.TryGetProperty("limits", out var limits)
            && limits.ValueKind is JsonValueKind.Object
            && limits.TryGetProperty(name, out var nested)
            && nested.TryGetInt32(out var nestedValue))
        {
            return nestedValue;
        }

        return capabilities.TryGetProperty(name, out var direct) && direct.TryGetInt32(out var value) ? value : null;
    }

    #endregion

    #region Nested Types

    private sealed record Candidate(bool PickerEnabled, string? PolicyState, ModelInfo Info);

    #endregion
}
