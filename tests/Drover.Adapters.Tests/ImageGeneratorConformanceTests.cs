namespace Drover.Adapters.Tests;

public sealed class CompatibleImageGeneratorConformanceTests : ImageGeneratorConformance
{
    #region Usage Validation

    [Theory]
    [InlineData(-1, 3000000000L, "-1", "USD")]
    [InlineData(3000000000L, -1, "1", "invalid currency")]
    public async Task Discards_invalid_costs_and_token_counts(long input, long output, string cost, string currency)
    {
        var handler = new RecordedHandler(RecordedFixture.Parse(
            $$"""
            {
              "provenance": "hand-written",
              "note": "Malformed accounting fields on a successful image response",
              "exchanges": [{
                "method": "POST",
                "path": "/images/generations",
                "body": {
                  "data": [{
                    "b64_json": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZzB8AAAAASUVORK5CYII="
                  }],
                  "usage": {
                    "input_tokens": {{input}}, "output_tokens": {{output}},
                    "cost": {{cost}}, "currency": "{{currency}}"
                  }
                }
              }]
            }
            """));
        var generator = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var result = await generator.Generate(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address),
            TestKeys.Generate(),
            new ModelImageRequest("A red square.", "picture-model", "1024x1024", 1024 * 1024),
            TestContext.Current.CancellationToken);

        result.Usage.Should().Be(new ModelImageUsage());
    }

    #endregion

    #region Adapter Hooks

    protected override ImageGeneratorUnderTest Start()
    {
        var handler = new RecordedHandler(RecordedFixture.Load("openai-compatible", "image_generation"));
        var generator = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        return new ImageGeneratorUnderTest(
            generator,
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address),
            TestKeys.Generate(),
            "picture-model",
            new ModelImageUsage { InputTokens = 12, OutputTokens = 34 },
            handler.Wire);
    }

    #endregion
}


public sealed class VeniceImageGeneratorConformanceTests : ImageGeneratorConformance
{
    #region Adapter Hooks

    protected override ImageGeneratorUnderTest Start()
    {
        var handler = new RecordedHandler(RecordedFixture.Load("venice", "image_generation"));
        var generator = ProviderBuilders.Venice(new FixtureHttpClientFactory(handler));
        return new ImageGeneratorUnderTest(
            generator,
            new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress),
            TestKeys.Generate(),
            "picture-model",
            new ModelImageUsage { Cost = 0.04m },
            handler.Wire);
    }

    #endregion
}


public sealed class VeniceImageGeneratorPricingTests
{
    #region Pricing

    [Fact]
    public async Task Leaves_tiered_image_catalog_pricing_unknown_instead_of_guessing()
    {
        var handler = new RecordedHandler(Fixture(
            """
            {
              "id": "picture-model",
              "model_spec": {
                "pricing": {
                  "generation": { "usd": 0.04 },
                  "resolutions": { "1K": { "usd": 0.04 } }
                }
              }
            }
            """));
        var provider = ProviderBuilders.Venice(new FixtureHttpClientFactory(handler));

        var result = await provider.Generate(
            new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress),
            TestKeys.Generate(),
            new ModelImageRequest("A red square.", "picture-model", "1024x1024", 1024 * 1024),
            TestContext.Current.CancellationToken);

        result.Usage.Cost.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_a_model_missing_from_the_current_image_catalog_without_generating()
    {
        var handler = new RecordedHandler(Fixture(
            """
            {
              "id": "another-model",
              "model_spec": { "pricing": { "generation": { "usd": 0.04 } } }
            }
            """));
        var provider = ProviderBuilders.Venice(new FixtureHttpClientFactory(handler));
        var generate = async () => await provider.Generate(
            new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress),
            TestKeys.Generate(),
            new ModelImageRequest("A red square.", "picture-model", "1024x1024", 1024 * 1024),
            TestContext.Current.CancellationToken);

        (await generate.Should().ThrowAsync<ModelProviderException>()).Which.Failure.Should().Be(ModelFailure.Rejected);
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    #endregion

    #region Private Methods

    private static RecordedFixture Fixture(string model) => RecordedFixture.Parse(
        $$$"""
        {
          "provenance": "hand-written",
          "note": "hand-written from the documented image catalog pricing shapes",
          "exchanges": [
            { "method": "GET", "path": "/models", "body": { "data": [{{{model}}}] } },
            {
              "method": "POST",
              "path": "/images/generations",
              "body": {
                "data": [
                  {
                    "b64_json":
                      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZzB8AAAAASUVORK5CYII="
                  }
                ]
              }
            }
          ]
        }
        """);

    #endregion
}
