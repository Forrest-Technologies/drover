namespace Drover.Adapters.Tests.Support;

/// <summary>
/// The provider's Messages wire: <c>POST /v1/messages</c> with <c>messages</c>, whose content is a list of blocks. A
/// tool's result is a <c>tool_result</c> block in a user message, structured output is <c>output_config.format</c> with
/// a schema and no name, and reasoning goes back as <c>thinking</c> blocks with their <c>signature</c>, or
/// <c>redacted_thinking</c> blocks with their <c>data</c>. Shapes from https://platform.claude.com/docs/en/api/messages,
/// https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview and
/// https://platform.claude.com/docs/en/build-with-claude/pdf-support (read 2026-11-27).
/// </summary>
public sealed class AnthropicWire : IChatWire
{
    #region Properties

    public static AnthropicWire Instance { get; } = new();

    public bool NamesSchemas => false;

    #endregion

    #region Interface Implementations

    public bool IsModelCall(WireRequest request) => request.Uri.AbsolutePath.EndsWith("/v1/messages", StringComparison.Ordinal);

    public IReadOnlyList<string> SentToolNames(WireRequest request) =>
        request.Json.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!)]
            : [];

    public string? SentToolResult(WireRequest request, string callId) =>
        Blocks(request, "tool_result")
            .Where(block => block.GetProperty("tool_use_id").GetString() == callId)
            .Select(block => block.GetProperty("content") is { ValueKind: JsonValueKind.String } content
                ? content.GetString()
                : string.Concat(block.GetProperty("content").EnumerateArray().Select(part => part.GetProperty("text").GetString())))
            .SingleOrDefault();

    public StructuredFormat SentStructuredFormat(WireRequest request) =>
        request.Json.TryGetProperty("output_config", out var config)
        && config.TryGetProperty("format", out var format)
        && format.GetProperty("type").GetString() == "json_schema"
            ? new(StructuredFormatKind.JsonSchema)
            : new(StructuredFormatKind.None);

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) =>
        [.. Blocks(request, "thinking", "redacted_thinking")
            .Select(block => block.GetProperty("type").GetString() == "thinking"
                ? block.GetProperty("signature").GetString()!
                : block.GetProperty("data").GetString()!)];

    public IReadOnlyList<string> SentPdfs(WireRequest request) =>
        [.. Blocks(request, "document")
            .Select(block => block.GetProperty("source"))
            .Where(source => source.GetProperty("type").GetString() == "base64"
                && source.GetProperty("media_type").GetString() == "application/pdf")
            .Select(source => source.GetProperty("data").GetString()!)];

    #endregion

    #region Public Methods

    /// <summary>Every content block of every message, of the given types, in order.</summary>
    public static IEnumerable<JsonElement> Blocks(WireRequest request, params string[] types) =>
        request.Json.TryGetProperty("messages", out var messages)
            ? messages.EnumerateArray()
                .Where(message => message.GetProperty("content").ValueKind is JsonValueKind.Array)
                .SelectMany(message => message.GetProperty("content").EnumerateArray())
                .Where(block => types.Contains(block.GetProperty("type").GetString()))
            : [];

    #endregion
}
