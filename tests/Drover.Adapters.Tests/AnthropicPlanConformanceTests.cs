namespace Drover.Adapters.Tests;

/// <summary>
/// The Claude plan provider through the shared model provider suite. A plan connection calls the same Messages API at
/// the same address as a key, with the person's token as the bearer credential, so it replays the key kind's
/// hand-written fixtures and makes the same claims.
/// </summary>
public sealed class AnthropicPlanConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(
        StructuredOutput: true,
        ReasoningText: true,
        ProtectedReasoning: true,
        CachedUsage: true,
        JsonObject: false,
        PdfInput: true);

    protected override ProviderUnderTest Start(string scenario) => AnthropicPlanRig.Start(scenario).Subject;

    #endregion
}
