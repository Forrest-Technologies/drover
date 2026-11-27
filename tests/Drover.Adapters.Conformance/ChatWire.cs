namespace Drover.Adapters.Conformance;

/// <summary>How a request asked for structured output: not at all, any JSON object, or a named schema.</summary>
public enum StructuredFormatKind
{
    None,
    JsonObject,
    JsonSchema,
}

/// <summary>The structured output a request asked for, read from the wire.</summary>
public sealed record StructuredFormat(StructuredFormatKind Kind, string? SchemaName = null);

/// <summary>
/// How one wire format carries what the suite checks, so the suite reads the wire without assuming one format.
/// Each adapter's tests give the wire its adapter speaks; <see cref="ChatCompletionsWire"/> is the default.
/// </summary>
public interface IChatWire
{
    /// <summary>True for a request that is a call to the model, as opposed to a model list or another read.</summary>
    bool IsModelCall(WireRequest request);

    /// <summary>The names of the tools a model call offered.</summary>
    IReadOnlyList<string> SentToolNames(WireRequest request);

    /// <summary>The text a model call sent back as the result of the tool call <paramref name="callId"/>, or null.</summary>
    string? SentToolResult(WireRequest request, string callId);

    StructuredFormat SentStructuredFormat(WireRequest request);

    /// <summary>The protected reasoning values a model call sent back, in order. Empty for a wire that has none.</summary>
    IReadOnlyList<string> SentProtectedReasoning(WireRequest request);
}

/// <summary>The common chat completions wire: <c>POST .../chat/completions</c> with <c>messages</c>.</summary>
public sealed class ChatCompletionsWire : IChatWire
{
    #region Properties

    public static ChatCompletionsWire Instance { get; } = new();

    #endregion

    #region Interface Implementations

    public bool IsModelCall(WireRequest request) => request.Uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal);

    public IReadOnlyList<string> SentToolNames(WireRequest request) =>
        request.Json.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray().Select(tool => tool.GetProperty("function").GetProperty("name").GetString()!)]
            : [];

    public string? SentToolResult(WireRequest request, string callId) =>
        request.Json.GetProperty("messages").EnumerateArray()
            .Where(message => message.GetProperty("role").GetString() == "tool"
                && message.GetProperty("tool_call_id").GetString() == callId)
            .Select(message => message.GetProperty("content").GetString())
            .SingleOrDefault();

    public StructuredFormat SentStructuredFormat(WireRequest request)
    {
        if (!request.Json.TryGetProperty("response_format", out var format))
        {
            return new(StructuredFormatKind.None);
        }

        return format.GetProperty("type").GetString() switch
        {
            "json_schema" => new(StructuredFormatKind.JsonSchema, format.GetProperty("json_schema").GetProperty("name").GetString()),
            "json_object" => new(StructuredFormatKind.JsonObject),
            _ => new(StructuredFormatKind.None),
        };
    }

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) => [];

    #endregion
}
