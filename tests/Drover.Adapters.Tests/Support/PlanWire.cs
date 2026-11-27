namespace Drover.Adapters.Tests.Support;

/// <summary>
/// The Responses wire as a ChatGPT plan takes it: the same items, with function tools grouped in a namespace
/// (https://developers.openai.com/siwc/llms-full.txt, "Preview limitations", read 2026-11-27).
/// </summary>
public sealed class PlanWire : IChatWire
{
    #region Properties

    public static PlanWire Instance { get; } = new();

    #endregion

    #region Interface Implementations

    public bool IsModelCall(WireRequest request) => ResponsesWire.Instance.IsModelCall(request);

    public IReadOnlyList<string> SentToolNames(WireRequest request) =>
        request.Json.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray()
                .Where(tool => tool.GetProperty("type").GetString() == "namespace")
                .SelectMany(group => group.GetProperty("tools").EnumerateArray())
                .Select(tool => tool.GetProperty("name").GetString()!)]
            : [];

    public string? SentToolResult(WireRequest request, string callId) => ResponsesWire.Instance.SentToolResult(request, callId);

    public StructuredFormat SentStructuredFormat(WireRequest request) => ResponsesWire.Instance.SentStructuredFormat(request);

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) => ResponsesWire.Instance.SentProtectedReasoning(request);

    #endregion
}
