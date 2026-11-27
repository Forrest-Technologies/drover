namespace Drover.Adapters.Gemini;

/// <summary>
/// Builds the body of <c>generateContent</c> and <c>streamGenerateContent</c> from Drover's own request. Drover keeps the
/// conversation, so the whole history goes in <c>contents</c> every time and <c>store</c> is false.
/// A thought signature travels as a <see cref="ModelReasoning"/> with <see cref="ModelReasoning.ProtectedData"/>
/// set, placed directly before the part it belongs to (a function call, or text). It goes back on that part, unchanged
/// and in the same order. Reasoning without protected data is words for people and is left out.
/// Sources (read 2026-11-27): https://ai.google.dev/api/generate-content (request body, GenerationConfig,
/// ThinkingConfig, <c>store</c>), https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/thinking/thought-signatures
/// (signature rules for REST: the first call part of a step carries it, all calls come first and then their responses
/// in the same order) and https://aistudio.google.com/docs/thinking.
/// </summary>
internal static class GeminiRequest
{
    #region Private Fields

    /// <summary>The provider allows at most five stop sequences.</summary>
    private const int MaxStopSequences = 5;

    private const string NoPictureWords = "This provider reads pictures, but not other files. Send the file's text instead.";

    #endregion

    #region Public Methods

    /// <summary>The bot's settings fill what the call leaves unset.</summary>
    public static ModelChatOptions WithDefaults(ModelChatOptions options, ModelSettings settings) => options with
    {
        Temperature = options.Temperature ?? (float?)settings.Temperature,
        MaxOutputTokens = options.MaxOutputTokens ?? settings.MaxOutputTokens,
        ReasoningEffort = options.ReasoningEffort ?? settings.ReasoningEffort,
        ResponseFormat = options.ResponseFormat ?? (settings.StructuredOutput ? "json" : null),
    };

    public static JsonObject Build(ModelChatRequest request, ModelSettings settings, bool askForThoughts)
    {
        var options = WithDefaults(request.Options, settings);
        var body = new JsonObject { ["contents"] = Contents(request.Messages), ["store"] = false };

        if (SystemInstruction(request, options) is { } instruction)
        {
            body["systemInstruction"] = instruction;
        }

        if (options.Tools is { Count: > 0 } tools)
        {
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = ToArray(tools.Select(Declaration)) });

            if (ToolConfig(options) is { } config)
            {
                body["toolConfig"] = config;
            }
        }

        var generation = GenerationConfig(options, askForThoughts);

        if (generation.Count > 0)
        {
            body["generationConfig"] = generation;
        }

        return body;
    }

    /// <summary>
    /// The wire's name for an effort: any value the provider documents, so a model's own refusal is the answer to one it
    /// lacks. "max" is the top level the provider offers.
    /// </summary>
    public static string? ThinkingLevel(string? effort) => effort?.ToLowerInvariant() switch
    {
        null or "" => null,
        "max" => "XHIGH",
        var level => level.ToUpperInvariant(),
    };

    /// <summary>True when the request asks only to include thoughts, which a model that does not think refuses.</summary>
    public static bool AsksOnlyForThoughts(ModelChatRequest request, ModelSettings settings) =>
        ThinkingLevel(WithDefaults(request.Options, settings).ReasoningEffort) is null;

    #endregion

    #region Contents

    private static JsonArray Contents(IReadOnlyList<ModelChatMessage> messages)
    {
        var names = messages.SelectMany(message => message.Contents).OfType<ModelToolCall>()
            .GroupBy(call => call.CallId).ToDictionary(group => group.Key, group => group.First().Name);
        List<(string Role, JsonArray Parts)> turns = [];
        List<string> calls = [];
        var index = 0;

        while (index < messages.Count)
        {
            var message = messages[index];

            if (message.Role == "tool")
            {
                // Every response to a step's calls is one turn after the calls, in the order of the calls.
                var results = new List<ModelToolResult>();

                while (index < messages.Count && messages[index].Role == "tool")
                {
                    results.AddRange(messages[index].Contents.OfType<ModelToolResult>());
                    index++;
                }

                var ordered = results.OrderBy(result => Math.Max(calls.IndexOf(result.CallId), 0));
                AddTurn(turns, "user", ToArray(ordered.Select(result => Response(result, names))));
                continue;
            }

            index++;

            if (message.Role == "system")
            {
                continue;
            }

            if (message.Role == "assistant")
            {
                calls = [.. message.Contents.OfType<ModelToolCall>().Select(call => call.CallId)];
                AddTurn(turns, "model", ModelParts(message.Contents));
            }
            else
            {
                AddTurn(turns, "user", UserParts(message.Contents));
            }
        }

        return ToArray(turns.Select(turn => new JsonObject { ["role"] = turn.Role, ["parts"] = turn.Parts }));
    }

    private static void AddTurn(List<(string Role, JsonArray Parts)> turns, string role, JsonArray parts)
    {
        if (parts.Count == 0)
        {
            return;
        }

        if (turns.Count > 0 && turns[^1].Role == role)
        {
            foreach (var part in parts.ToList())
            {
                parts.Remove(part);
                turns[^1].Parts.Add(part);
            }

            return;
        }

        turns.Add((role, parts));
    }

    private static JsonArray UserParts(IReadOnlyList<ModelContent> contents)
    {
        JsonArray parts = [];

        foreach (var content in contents)
        {
            switch (content)
            {
                case ModelText text:
                    parts.Add(new JsonObject { ["text"] = text.Text });
                    break;
                case ModelData data when IsPicture(data.MediaType):
                    parts.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject { ["mimeType"] = data.MediaType, ["data"] = Convert.ToBase64String(data.Data.Span) },
                    });
                    break;
                case ModelData or ModelUri:
                    throw ModelProviderException.FromRefusal(NoPictureWords);
            }
        }

        return parts;
    }

    private static JsonArray ModelParts(IReadOnlyList<ModelContent> contents)
    {
        JsonArray parts = [];
        string? signature = null;

        foreach (var content in contents)
        {
            switch (content)
            {
                case ModelReasoning { ProtectedData: { } sealedThought }:
                    signature = sealedThought;
                    break;
                case ModelText { Text.Length: > 0 } text:
                    parts.Add(WithSignature(new JsonObject { ["text"] = text.Text }, ref signature));
                    break;
                case ModelToolCall call:
                    parts.Add(WithSignature(FunctionCall(call), ref signature));
                    break;
            }
        }

        return parts;
    }

    private static JsonObject WithSignature(JsonObject part, ref string? signature)
    {
        if (signature is not null)
        {
            part["thoughtSignature"] = signature;
            signature = null;
        }

        return part;
    }

    private static JsonObject FunctionCall(ModelToolCall call)
    {
        var arguments = call.Arguments.ValueKind == JsonValueKind.Object ? call.Arguments.GetRawText() : "{}";
        var inner = new JsonObject { ["name"] = call.Name, ["args"] = JsonNode.Parse(arguments) };

        // An id Drover made up for a call the provider gave none is not the provider's to see.
        if (!GeminiReply.IsMadeUp(call.CallId))
        {
            inner["id"] = call.CallId;
        }

        return new JsonObject { ["functionCall"] = inner };
    }

    private static JsonObject Response(ModelToolResult result, Dictionary<string, string> names)
    {
        var value = JsonNode.Parse(result.Result.ValueKind == JsonValueKind.Undefined ? "null" : result.Result.GetRawText());
        var response = value is JsonObject record && !result.HasError
            ? record
            : new JsonObject { [result.HasError ? "error" : "result"] = value };
        var inner = new JsonObject { ["name"] = names.GetValueOrDefault(result.CallId, "tool"), ["response"] = response };

        if (!GeminiReply.IsMadeUp(result.CallId))
        {
            inner["id"] = result.CallId;
        }

        return new JsonObject { ["functionResponse"] = inner };
    }

    private static JsonArray ToArray(IEnumerable<JsonNode> items)
    {
        var array = new JsonArray();

        foreach (var item in items)
        {
            array.Add(item);
        }

        return array;
    }

    private static bool IsPicture(string mediaType) => mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Settings

    private static JsonObject? SystemInstruction(ModelChatRequest request, ModelChatOptions options)
    {
        var words = new[] { options.Instructions }
            .Concat(request.Messages.Where(message => message.Role == "system")
                .SelectMany(message => message.Contents.OfType<ModelText>().Select(text => text.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return words.Count == 0
            ? null
            : new JsonObject { ["parts"] = ToArray(words.Select(text => new JsonObject { ["text"] = text })) };
    }

    private static JsonObject Declaration(ModelTool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["parametersJsonSchema"] = JsonNode.Parse(tool.Schema.GetRawText()),
    };

    private static JsonObject? ToolConfig(ModelChatOptions options)
    {
        var mode = options.ToolMode switch
        {
            "required" => "ANY",
            "none" => "NONE",
            _ => null,
        };

        if (mode is null)
        {
            return null;
        }

        var config = new JsonObject { ["mode"] = mode };

        if (mode == "ANY" && options.RequiredToolName is { } name)
        {
            config["allowedFunctionNames"] = new JsonArray(name);
        }

        return new JsonObject { ["functionCallingConfig"] = config };
    }

    private static JsonObject GenerationConfig(ModelChatOptions options, bool askForThoughts)
    {
        var config = new JsonObject();

        Set(config, "maxOutputTokens", options.MaxOutputTokens);
        Set(config, "temperature", options.Temperature);
        Set(config, "topP", options.TopP);
        Set(config, "topK", options.TopK);
        Set(config, "seed", options.Seed);

        if (options.StopSequences is { Count: > 0 } stops)
        {
            config["stopSequences"] = ToArray(stops.Take(MaxStopSequences).Select(stop => JsonValue.Create(stop)));
        }

        if (options.ResponseSchema is { } schema)
        {
            config["responseMimeType"] = "application/json";
            config["responseJsonSchema"] = JsonNode.Parse(schema.GetRawText());
        }
        else if (options.ResponseFormat == "json")
        {
            config["responseMimeType"] = "application/json";
        }

        var level = ThinkingLevel(options.ReasoningEffort);

        if (askForThoughts || level is not null)
        {
            var thinking = new JsonObject { ["includeThoughts"] = true };

            if (level is not null)
            {
                thinking["thinkingLevel"] = level;
            }

            config["thinkingConfig"] = thinking;
        }

        return config;
    }

    private static void Set<T>(JsonObject config, string name, T? value)
        where T : struct
    {
        if (value is { } found)
        {
            config[name] = JsonValue.Create(found);
        }
    }

    #endregion
}
