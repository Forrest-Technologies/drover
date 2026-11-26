namespace Drover.Adapters.Tests.Support;

/// <summary>
/// The provider's Responses wire: <c>POST .../responses</c> with <c>input</c> items. Shapes from
/// https://developers.openai.com/api/reference/resources/responses/methods/create and
/// https://developers.openai.com/api/docs/guides/function-calling (read 2026-11-26).
/// </summary>
public sealed class ResponsesWire : IChatWire
{
    #region Properties

    public static ResponsesWire Instance { get; } = new();

    #endregion

    #region Interface Implementations

    public bool IsModelCall(WireRequest request) => request.Uri.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal);

    public IReadOnlyList<string> SentToolNames(WireRequest request) =>
        request.Json.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray()
                .Where(tool => tool.GetProperty("type").GetString() == "function")
                .Select(tool => tool.GetProperty("name").GetString()!)]
            : [];

    public string? SentToolResult(WireRequest request, string callId) =>
        Items(request, "function_call_output")
            .Where(item => item.GetProperty("call_id").GetString() == callId)
            .Select(item => item.GetProperty("output").GetString())
            .SingleOrDefault();

    public StructuredFormat SentStructuredFormat(WireRequest request)
    {
        if (!request.Json.TryGetProperty("text", out var text) || !text.TryGetProperty("format", out var format))
        {
            return new(StructuredFormatKind.None);
        }

        return format.GetProperty("type").GetString() switch
        {
            "json_schema" => new(StructuredFormatKind.JsonSchema, format.GetProperty("name").GetString()),
            "json_object" => new(StructuredFormatKind.JsonObject),
            _ => new(StructuredFormatKind.None),
        };
    }

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) =>
        [.. Items(request, "reasoning")
            .Where(item => item.TryGetProperty("encrypted_content", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("encrypted_content").GetString()!)];

    #endregion

    #region Private Methods

    private static IEnumerable<JsonElement> Items(WireRequest request, string type) =>
        request.Json.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array
            ? input.EnumerateArray().Where(item => item.TryGetProperty("type", out var kind) && kind.GetString() == type)
            : [];

    #endregion
}
