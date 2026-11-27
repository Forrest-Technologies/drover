namespace Drover.Adapters.Tests;

/// <summary>
/// Anthropic on its Messages API through the shared suite. Its model list says what each model can do, and the test
/// model reads schemas, PDFs and pictures and thinks adaptively; the provider answers JSON only to a schema, so a bot
/// set to JSON-only is refused in words. The fixtures are hand-written from the provider's reference.
/// </summary>
public sealed class AnthropicConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(
        StructuredOutput: true,
        ReasoningText: true,
        ProtectedReasoning: true,
        CachedUsage: true,
        JsonObject: false,
        PdfInput: true);

    protected override ProviderUnderTest Start(string scenario) => AnthropicRig.Start(scenario).Subject;

    #endregion
}
