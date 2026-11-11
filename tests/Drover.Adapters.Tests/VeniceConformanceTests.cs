namespace Drover.Adapters.Tests;

/// <summary>The Venice adapter through the shared suite. The test model supports structured output and reasoning.</summary>
public sealed class VeniceConformanceTests : ModelProviderConformance
{
    protected override ConformanceClaims Claims => new(StructuredOutput: true, ReasoningText: true);

    protected override ProviderUnderTest Start(string scenario) => VeniceRig.Start(scenario).Subject;
}
