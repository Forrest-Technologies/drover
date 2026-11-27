namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Shapes every model call on a person's ChatGPT plan into what the plan accepts, on the request body itself, so
/// nothing the vendor client adds can get past it. Source, read 2026-11-27: https://developers.openai.com/siwc/llms-full.txt,
/// "Preview limitations" ("Responses API requirements") and "Models and inference".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>store: false</c> and <c>stream: true</c> on every request.</item>
/// <item>No system-role item: the plan refuses them. The words of each go to <c>instructions</c>, after any already
/// there, so the bot's instructions travel where the plan takes them.</item>
/// <item>None of the fields the plan does not take (<see cref="refusedFields"/>, copied from the page), and no
/// <c>previous_response_id</c>: Drover sends the whole conversation each time.</item>
/// <item>Function tools grouped in one namespace (<see cref="ToolNamespace"/>), as the page asks, and each function
/// call sent back names it. The page does not show a call coming back from a namespace, so this follows the
/// provider's function calling guide (https://developers.openai.com/api/docs/guides/function-calling, "Defining
/// namespaces") and its SDK's <c>function_call.namespace</c> field; it is unconfirmed against a live plan.</item>
/// </list>
/// A sign-in by code (<see cref="ByCode"/>) is used at the address the provider's own command line tool calls, which
/// takes the tool's request shape: the same rules, the function tools as they are, no namespace, an
/// <c>instructions</c> field always present, and <c>include: ["reasoning.encrypted_content"]</c> so reasoning travels
/// back with <c>store: false</c> (read from the tool's published source on 2026-11-27).
/// </remarks>
internal sealed class PlanRequestShaping(bool byCode) : PipelinePolicy
{
    #region Private Fields

    /// <summary>The namespace Drover's tools are offered in.</summary>
    public const string ToolNamespace = "drover";

    private const string ToolNamespaceDescription = "The tools this bot may use.";

    /// <summary>"Unsupported fields: Omit ..." on the preview limitations page, and the conversation link it also forbids.</summary>
    private static readonly string[] refusedFields =
    [
        "background",
        "conversation",
        "max_output_tokens",
        "max_tool_calls",
        "metadata",
        "moderation",
        "multi_agent",
        "prompt",
        "prompt_cache_retention",
        "safety_identifier",
        "temperature",
        "top_logprobs",
        "top_p",
        "truncation",
        "user",
        "previous_response_id",
    ];

    #endregion

    #region Properties

    public static PlanRequestShaping Instance { get; } = new(byCode: false);

    /// <summary>The shaping for a plan signed in by code.</summary>
    public static PlanRequestShaping ByCode { get; } = new(byCode: true);

    #endregion

    #region Public Methods

    /// <summary>The settings the plan refuses, left out before the request is built.</summary>
    public static ModelChatOptions LeaveOutRefusedSettings(ModelChatOptions options) =>
        options with
        {
            MaxOutputTokens = null,
            Temperature = null,
            TopP = null,
            TopK = null,
            FrequencyPenalty = null,
            PresencePenalty = null,
            Seed = null,
        };

    /// <summary>The request body as the plan takes it. A body that is not a JSON object is left alone.</summary>
    public static string Shape(string body) => Instance.ShapeBody(body);

    /// <summary>The request body as the plan takes it on this policy's flow. A body that is not a JSON object is left alone.</summary>
    public string ShapeBody(string body)
    {
        if (JsonNode.Parse(body) is not JsonObject root)
        {
            return body;
        }

        foreach (var field in refusedFields)
        {
            root.Remove(field);
        }

        root["store"] = false;
        root["stream"] = true;
        MoveSystemItemsToInstructions(root);

        if (byCode)
        {
            root["instructions"] ??= string.Empty;
            root["include"] = new JsonArray("reasoning.encrypted_content");
            return root.ToJsonString();
        }

        var namespaced = GroupTools(root);
        NameTheNamespaceOnCalls(root, namespaced);
        return root.ToJsonString();
    }

    #endregion

    #region Interface Implementations

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Rewrite(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Rewrite(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    #endregion

    #region Shaping

    private void Rewrite(PipelineMessage message)
    {
        if (message.Request.Method != "POST" || message.Request.Content is not { } content
            || message.Request.Uri?.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal) != true)
        {
            return;
        }

        using var buffer = new MemoryStream();
        content.WriteTo(buffer, message.CancellationToken);
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(ShapeBody(Encoding.UTF8.GetString(buffer.ToArray()))));
    }

    private static void MoveSystemItemsToInstructions(JsonObject root)
    {
        if (root["input"] is not JsonArray input)
        {
            return;
        }

        List<string> words = root["instructions"]?.GetValueKind() is JsonValueKind.String ? [root["instructions"]!.GetValue<string>()] : [];

        foreach (var item in input.OfType<JsonObject>().Where(IsSystemItem).ToList())
        {
            words.Add(TextOf(item["content"]));
            input.Remove(item);
        }

        var joined = string.Join("\n\n", words.Where(text => text.Length > 0));

        if (joined.Length > 0)
        {
            root["instructions"] = joined;
        }
    }

    private static bool IsSystemItem(JsonObject item) =>
        item["role"]?.GetValueKind() is JsonValueKind.String && item["role"]!.GetValue<string>() == "system"
        && (item["type"] is null || (item["type"]!.GetValueKind() is JsonValueKind.String && item["type"]!.GetValue<string>() == "message"));

    private static string TextOf(JsonNode? content) =>
        content switch
        {
            JsonValue value when value.GetValueKind() is JsonValueKind.String => value.GetValue<string>(),
            JsonArray parts => string.Join("\n", parts.OfType<JsonObject>()
                .Select(part => part["text"])
                .Where(text => text?.GetValueKind() is JsonValueKind.String)
                .Select(text => text!.GetValue<string>())),
            _ => string.Empty,
        };

    /// <summary>Puts every function tool in one namespace and returns the names it holds.</summary>
    private static HashSet<string> GroupTools(JsonObject root)
    {
        HashSet<string> names = [with(StringComparer.Ordinal)];

        if (root["tools"] is not JsonArray tools)
        {
            return names;
        }

        var functions = tools.OfType<JsonObject>().Where(tool => TypeOf(tool) is "function" or "custom").ToList();

        if (functions.Count == 0)
        {
            return names;
        }

        JsonArray grouped = [];

        foreach (var function in functions)
        {
            tools.Remove(function);
            grouped.Add(function);

            if (function["name"]?.GetValueKind() is JsonValueKind.String)
            {
                names.Add(function["name"]!.GetValue<string>());
            }
        }

        tools.Insert(0, new JsonObject
        {
            ["type"] = "namespace",
            ["name"] = ToolNamespace,
            ["description"] = ToolNamespaceDescription,
            ["tools"] = grouped,
        });
        return names;
    }

    private static void NameTheNamespaceOnCalls(JsonObject root, HashSet<string> namespaced)
    {
        if (root["input"] is not JsonArray input || namespaced.Count == 0)
        {
            return;
        }

        foreach (var call in input.OfType<JsonObject>().Where(item => TypeOf(item) == "function_call"))
        {
            if (call["name"]?.GetValueKind() is JsonValueKind.String && namespaced.Contains(call["name"]!.GetValue<string>()))
            {
                call["namespace"] = ToolNamespace;
            }
        }
    }

    private static string? TypeOf(JsonObject item) =>
        item["type"]?.GetValueKind() is JsonValueKind.String ? item["type"]!.GetValue<string>() : null;

    #endregion
}
