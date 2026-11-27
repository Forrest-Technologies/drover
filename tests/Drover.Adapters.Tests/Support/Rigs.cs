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

public static class OpenAIPlanRig
{
    #region Public Methods

    public const string ModelId = "gpt-test-reasoner";

    /// <summary>A plan connection on the default settings, wired to a fixture under Fixtures/openai-plan.</summary>
    public static Rig Start(string fixtureName) => Start(fixtureName, new SettableOptions<OpenAIPlanOptions>(new()));

    public static Rig Start(string fixtureName, IOptionsMonitor<OpenAIPlanOptions> options)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("openai-plan", fixtureName));
        var provider = new OpenAIPlanProvider(new FixtureHttpClientFactory(handler), options);
        var connection = new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, options.CurrentValue.ApiAddress);
        var subject = new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire) { Wire = PlanWire.Instance };

        return new Rig(subject, handler);
    }

    #endregion
}

public static class OpenAIResponsesRig
{
    #region Public Methods

    public const string ModelId = "gpt-test-reasoner";

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("openai-responses", fixtureName));
        var provider = new OpenAIProvider(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(OpenAIProvider.ConnectionKind, OpenAIProvider.BaseAddress);
        var subject = new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire) { Wire = ResponsesWire.Instance };

        return new Rig(subject, handler);
    }

    #endregion
}

public static class AnthropicRig
{
    #region Public Methods

    public const string ModelId = "claude-test-model";

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("anthropic", fixtureName));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(AnthropicProvider.ConnectionKind, AnthropicProvider.BaseAddress);
        var subject = new ProviderUnderTest(provider, connection, TestKeys.Generate(), ModelId, handler.Wire) { Wire = AnthropicWire.Instance };

        return new Rig(subject, handler);
    }

    #endregion
}
