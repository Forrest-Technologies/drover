namespace Drover.Adapters.Tests.Support;

/// <summary>
/// The provider's generateContent wire: <c>POST .../models/{model}:generateContent</c> (or <c>:streamGenerateContent</c>)
/// with <c>contents</c> of parts. Shapes from https://ai.google.dev/api/generate-content (read 2026-11-27). The provider's
/// schema request has no name, so the wire reports none.
/// </summary>
public sealed class GeminiWire : IChatWire
{
    #region Properties

    public static GeminiWire Instance { get; } = new();

    #endregion

    #region Interface Implementations

    public bool IsModelCall(WireRequest request) =>
        request.Uri.AbsolutePath.EndsWith(":generateContent", StringComparison.Ordinal)
        || request.Uri.AbsolutePath.EndsWith(":streamGenerateContent", StringComparison.Ordinal);

    public IReadOnlyList<string> SentToolNames(WireRequest request) =>
        request.Json.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray()
                .SelectMany(tool => tool.GetProperty("functionDeclarations").EnumerateArray())
                .Select(declaration => declaration.GetProperty("name").GetString()!)]
            : [];

    public string? SentToolResult(WireRequest request, string callId) =>
        Parts(request)
            .Where(part => part.TryGetProperty("functionResponse", out var response)
                && response.TryGetProperty("id", out var id) && id.GetString() == callId)
            .Select(part => part.GetProperty("functionResponse").GetProperty("response").GetProperty("result").GetString())
            .SingleOrDefault();

    public StructuredFormat SentStructuredFormat(WireRequest request)
    {
        if (!request.Json.TryGetProperty("generationConfig", out var config)
            || !config.TryGetProperty("responseMimeType", out var mime) || mime.GetString() != "application/json")
        {
            return new(StructuredFormatKind.None);
        }

        return new(config.TryGetProperty("responseJsonSchema", out _) ? StructuredFormatKind.JsonSchema : StructuredFormatKind.JsonObject);
    }

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) =>
        [.. Parts(request).Where(part => part.TryGetProperty("thoughtSignature", out _)).Select(part => part.GetProperty("thoughtSignature").GetString()!)];

    #endregion

    #region Private Methods

    private static IEnumerable<JsonElement> Parts(WireRequest request) =>
        request.Json.TryGetProperty("contents", out var contents)
            ? contents.EnumerateArray().SelectMany(content => content.GetProperty("parts").EnumerateArray())
            : [];

    #endregion
}
