namespace Drover.Adapters.Tests;

/// <summary>The compatible adapter through the shared suite. It claims nothing beyond tools until told more.</summary>
public sealed class OpenAICompatibleConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: false, ReasoningText: false);

    protected override ProviderUnderTest Start(string scenario) => CompatibleRig.Start(scenario).Subject;

    #endregion
}
