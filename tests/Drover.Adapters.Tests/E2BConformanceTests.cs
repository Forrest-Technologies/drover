namespace Drover.Adapters.Tests;

/// <summary>The E2B adapter through the shared sandbox suite, against recorded answers in Fixtures/e2b.</summary>
public sealed class E2BConformanceTests : SandboxProviderConformance
{
    #region Public Methods

    [Theory]
    [MemberData(nameof(Scenarios), MemberType = typeof(SandboxProviderConformance))]
    public Task Passes_the_sandbox_suite(string scenario) => RunScenario(scenario);

    #endregion

    #region Adapter Hooks

    protected override SandboxUnderTest Start(string scenario) => E2BRig.Start(scenario).Subject;

    #endregion
}
