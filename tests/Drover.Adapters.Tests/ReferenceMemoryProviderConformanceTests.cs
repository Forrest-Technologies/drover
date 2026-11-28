namespace Drover.Adapters.Tests;

/// <summary>
/// The memory provider conformance suite run against the in-memory reference provider, proving the suite passes for a
/// provider that keeps every promise before any real adapter is held to it.
/// </summary>
public sealed class ReferenceMemoryProviderConformanceTests : MemoryProviderConformance
{
    #region Protected Methods

    protected override Task<MemoryProviderUnderTest> CreateProvider(string apiKey)
    {
        var provider = new FakeMemoryProvider(apiKey: apiKey);
        return Task.FromResult(new MemoryProviderUnderTest(
            provider,
            apiKey,
            () => provider.SentIdentifiers,
            failure => provider.FailAllWith = failure));
    }

    #endregion
}
