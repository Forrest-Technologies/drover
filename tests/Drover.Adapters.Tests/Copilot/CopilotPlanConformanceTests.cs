namespace Drover.Adapters.Tests;

/// <summary>The Copilot plan adapter through the shared provider suite. It claims tools and streaming, not structured output or reasoning text.</summary>
public sealed class CopilotPlanConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: false, ReasoningText: false);

    protected override ProviderUnderTest Start(string scenario) => CopilotRig.Start(scenario).Subject;

    #endregion
}
