namespace Drover.Adapters.Conformance;

/// <summary>
/// The situations every adapter must answer. Each adapter's tests serve canned responses for them.
/// The user prompts are fixed so a response can be recorded once: see the constants on the suite.
/// </summary>
public static class ConformanceScenarios
{
    public const string ListModels = "list_models";

    public const string TestConnection = "test_connection";

    public const string PlainReply = "plain_reply";

    public const string Streaming = "streaming";

    public const string ToolCall = "tool_call";

    public const string StructuredOutput = "structured_output";

    public const string ReasoningText = "reasoning_text";

    public const string RateLimited = "rate_limited";

    public const string InvalidKey = "invalid_key";
}
