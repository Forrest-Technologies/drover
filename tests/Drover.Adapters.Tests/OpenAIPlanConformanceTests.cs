namespace Drover.Adapters.Tests;

/// <summary>
/// A person's ChatGPT plan through the shared suite. Every call streams on this wire, so a whole answer is the stream
/// put together; the fixtures under Fixtures/openai-plan are the Responses fixtures as streams, with the plan's own
/// model list. Its model list says nothing about a model, so every model claims what the key kind's models claim.
/// </summary>
public sealed class OpenAIPlanConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: true, ReasoningText: true, ProtectedReasoning: true, CachedUsage: true);

    protected override ProviderUnderTest Start(string scenario) => OpenAIPlanRig.Start(scenario).Subject;

    #endregion
}
