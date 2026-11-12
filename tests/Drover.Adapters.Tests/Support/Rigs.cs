namespace Drover.Adapters.Tests.Support;

/// <summary>An adapter wired to a fixture, with the handler kept so the test can inspect what was sent.</summary>
public sealed record Rig(ProviderUnderTest Subject, RecordedHandler Handler);

public static class VeniceRig
{
    #region Public Methods

    public const string ModelId = "kimi-k3";

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("venice", fixtureName));
        var provider = ProviderBuilders.Venice(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress);

        return new Rig(new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire), handler);
    }

    #endregion
}

public static class CompatibleRig
{
    #region Public Methods

    public const string ModelId = "local-model";

    public static readonly Uri Address = new("https://models.example.test/v1");

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("openai-compatible", fixtureName));
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, Address);

        return new Rig(new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire), handler);
    }

    #endregion
}
