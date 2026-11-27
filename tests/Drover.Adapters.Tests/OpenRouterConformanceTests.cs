namespace Drover.Adapters.Tests;

/// <summary>
/// OpenRouter called with the key a sign-in made, through the shared provider suite. It speaks the common chat
/// protocol and its model list says nothing about a model, so it claims no more than tools.
/// </summary>
public sealed class OpenRouterConformanceTests : ModelProviderConformance
{
    #region Public Methods

    protected override ConformanceClaims Claims => new(StructuredOutput: false, ReasoningText: false, ProtectedReasoning: false, CachedUsage: false);

    protected override ProviderUnderTest Start(string scenario) => OpenRouterRig.Start(scenario).Subject;

    #endregion

    #region Wire

    [Fact]
    public async Task A_run_calls_the_fixed_address_with_the_persons_key_as_a_bearer_and_no_other_credential()
    {
        var rig = OpenRouterRig.Start("plain_reply");
        var client = rig.Subject.Provider.CreateChatClient(
            new ModelEndpoint(OpenRouterProvider.ConnectionKind, new Uri("https://elsewhere.example.test/v1")),
            rig.Subject.ApiKey,
            OpenRouterRig.ModelId,
            new ModelSettings());

        await client.Ask("Hi");

        var sent = rig.Handler.Requests.Should().ContainSingle().Subject;
        sent.Uri.Host.Should().Be("openrouter.ai", "the connection's own address is never used for this kind");
        sent.Headers["Authorization"].Should().Be($"Bearer {rig.Subject.ApiKey}");
    }

    [Fact]
    public async Task A_call_with_no_key_is_refused_before_anything_is_sent()
    {
        var rig = OpenRouterRig.Start("plain_reply");

        var creating = () => rig.Subject.Provider.CreateChatClient(rig.Subject.Connection, null, OpenRouterRig.ModelId, new ModelSettings());

        creating.Should().Throw<ModelProviderException>().Which.Failure.Should().Be(ModelFailure.InvalidKey);
        rig.Handler.Requests.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public void The_kind_asks_for_no_address_and_its_address_follows_the_setting()
    {
        var settings = new ChangingOptions<OpenRouterSignInOptions>(new OpenRouterSignInOptions());
        var provider = new OpenRouterProvider(new FixtureHttpClientFactory(new ScriptedHandler((_, _, _) => throw new InvalidOperationException())), settings);

        provider.Descriptor.KeyRule.Should().Be(ModelKeyRule.Required, "a call is refused without the key the sign-in made");
        provider.Descriptor.AddressRule.Should().Be(ModelAddressRule.Fixed);
        provider.FixedAddress.Should().Be(new Uri("https://openrouter.ai/api/v1/"));
        settings.CurrentValue = new OpenRouterSignInOptions { ApiAddress = new Uri("https://api.example.test/v9/") };
        provider.FixedAddress.Should().Be(new Uri("https://api.example.test/v9/"));
    }

    #endregion
}
