namespace Drover.Adapters.Tests;

/// <summary>
/// OpenAI on its Responses API through the shared suite. Its model list says nothing about a model, so every model
/// claims structured output and reasoning; the fixtures carry reasoning summaries, encrypted reasoning and cached usage.
/// </summary>
public sealed class OpenAIResponsesConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: true, ReasoningText: true, ProtectedReasoning: true, CachedUsage: true);

    protected override ProviderUnderTest Start(string scenario) => OpenAIResponsesRig.Start(scenario).Subject;

    #endregion
}
