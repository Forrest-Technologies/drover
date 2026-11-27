namespace Drover.Adapters.Tests.Support;

public static class GeminiRig
{
    #region Public Methods

    public const string ModelId = "gemini-test-thinker";

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("gemini", fixtureName));
        var provider = new GeminiProvider(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(GeminiProvider.ConnectionKind, GeminiProvider.BaseAddress);
        var subject = new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire) { Wire = GeminiWire.Instance };

        return new Rig(subject, handler);
    }

    #endregion
}
