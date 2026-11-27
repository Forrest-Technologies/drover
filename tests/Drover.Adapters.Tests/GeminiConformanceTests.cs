namespace Drover.Adapters.Tests;

/// <summary>
/// Google Gemini on <c>generateContent</c> through the shared suite. The test model thinks, so it claims reasoning text,
/// protected reasoning (the thought signature), structured output and cached usage.
/// </summary>
public sealed class GeminiConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: true, ReasoningText: true, ProtectedReasoning: true, CachedUsage: true);

    protected override ProviderUnderTest Start(string scenario) => GeminiRig.Start(scenario).Subject;

    #endregion
}
