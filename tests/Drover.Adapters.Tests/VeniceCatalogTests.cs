namespace Drover.Adapters.Tests;

/// <summary>Venice's public model list, read into Drover's capability record. The list is a real keyless capture.</summary>
public sealed class VeniceCatalogTests
{
    #region Capabilities

    [Fact]
    public async Task Fills_the_capability_record_from_the_model_list()
    {
        var models = await ListModels();

        var kimi = models.Single(model => model.Id == "kimi-k3");

        kimi.DisplayName.Should().Be("Kimi K3");
        kimi.Capabilities.Should().BeEquivalentTo(new ModelCapabilities
        {
            ContextTokens = 1_000_000,
            MaxOutputTokens = 131_072,
            ToolCalling = true,
            Vision = true,
            Reasoning = true,
            ReasoningEfforts = ["none", "low", "high", "max"],
            StructuredOutput = true,
            WebSearch = true,
            InputPricePerMillion = 3.75m,
            OutputPricePerMillion = 18.75m,
            Privacy = PrivacyMode.Private,
        });
    }

    [Fact]
    public async Task Offers_no_effort_levels_for_a_model_that_cannot_take_one()
    {
        var models = await ListModels();

        var plain = models.Single(model => model.Id == "venice-uncensored-1-2");

        plain.Capabilities.Reasoning.Should().BeFalse();
        plain.Capabilities.ReasoningEfforts.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_the_privacy_mode_in_the_providers_terms()
    {
        var models = await ListModels();

        models.Single(model => model.Id == "gemini-4-argon").Capabilities.Privacy.Should().Be(PrivacyMode.Anonymized);
        models.Single(model => model.Id == "kimi-k3").Capabilities.Privacy.Should().Be(PrivacyMode.Private);
        models.Single(model => model.Id == "e2ee-kimi-k3-p").Capabilities.Privacy.Should().Be(PrivacyMode.Attested);
    }

    #endregion

    #region Traits

    [Fact]
    public async Task Marks_the_providers_own_picks_as_traits()
    {
        var models = await ListModels();

        models.Single(model => model.Id == "kimi-k3").Traits.Should().Contain("default_reasoning");
        models.Single(model => model.Id == "deepseek-v4-1-flash-1017-uncensored").Traits.Should().Contain("default");
        models.Single(model => model.Id == "qwen-3-8-27b").Traits.Should().Contain("default_vision");
        models.Single(model => model.Id == "gemini-4-argon").Traits.Should().BeEmpty();
    }

    [Fact]
    public async Task Still_lists_models_when_the_trait_list_is_unavailable()
    {
        var provider = ProviderFor(
            """
            {"provenance":"hand-written","note":"hand-written: inline for one test","exchanges":[
              {"path":"/models","body":{"data":[{"id":"m1","model_spec":{"name":"One","capabilities":{}}}]}},
              {"path":"/models/traits","status":500,"body":{"error":"down"}}]}
            """);

        var models = await provider.ListModels(connection, null, TestContext.Current.CancellationToken);

        models.Select(model => model.Id).Should().Equal("m1");
    }

    [Fact]
    public async Task Leaves_out_models_the_provider_marks_offline()
    {
        var provider = ProviderFor(
            """
            {"provenance":"hand-written","note":"hand-written: inline for one test","exchanges":[
              {"path":"/models","body":{"data":[
                {"id":"up","model_spec":{"name":"Up","capabilities":{}}},
                {"id":"down","model_spec":{"name":"Down","offline":true,"capabilities":{}}}]}},
              {"path":"/models/traits","body":{"data":{}}}]}
            """);

        var models = await provider.ListModels(connection, null, TestContext.Current.CancellationToken);

        models.Select(model => model.Id).Should().Equal("up");
    }

    #endregion

    #region Connection Test

    [Fact]
    public async Task Reports_the_balances_when_the_key_works()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.TestConnection);

        var result = await rig.Subject.Provider.TestConnection(
            rig.Subject.Connection,
            rig.Subject.ApiKey,
            TestContext.Current.CancellationToken);

        result.Balances.Should().BeEquivalentTo(
            [new ModelBalance("usd", 50.23m), new ModelBalance("diem", 100.023m), new ModelBalance("bundled_credits", 25m)]);
        rig.Handler.Requests.Single().Headers["Authorization"].Should().Be($"Bearer {rig.Subject.ApiKey}");
    }

    [Fact]
    public async Task Asks_for_a_key_when_none_was_given()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.TestConnection);

        var result = await rig.Subject.Provider.TestConnection(
            rig.Subject.Connection,
            null,
            TestContext.Current.CancellationToken);

        result.Failure.Should().Be(ModelFailure.InvalidKey);
        rig.Handler.Requests.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static readonly ModelConnection connection = new(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress);

    private static async Task<IReadOnlyList<ModelInfo>> ListModels()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.ListModels);

        return await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);
    }

    private static VeniceProvider ProviderFor(string fixtureJson) =>
        new(new FixtureHttpClientFactory(new RecordedHandler(RecordedFixture.Parse(fixtureJson))));

    #endregion
}
