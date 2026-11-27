namespace Drover.Adapters.Tests;

/// <summary>
/// What the model list says becomes each model's record, and nothing else does. Shapes from
/// https://platform.claude.com/docs/en/api/models/list (read 2026-11-27); the PDF bounds from
/// https://platform.claude.com/docs/en/build-with-claude/pdf-support.
/// </summary>
public sealed class AnthropicCatalogTests
{
    #region Records

    [Fact]
    public async Task Each_model_says_what_it_can_do_and_what_it_does_not_say_is_false()
    {
        var subject = AnthropicRig.Start("list_models").Subject;

        var models = await subject.Provider.ListModels(subject.Connection, subject.ApiKey, Token);
        var large = models.Single(model => model.Id == "claude-test-model");
        var small = models.Single(model => model.Id == "claude-small-test-model");

        large.DisplayName.Should().Be("Claude Test Model");
        large.Capabilities.Should().BeEquivalentTo(new ModelCapabilities
        {
            ContextTokens = 1_000_000,
            MaxOutputTokens = 128_000,
            Vision = true,
            Reasoning = true,
            ReasoningEfforts = ["low", "medium", "high", "xhigh", "max"],
            StructuredOutput = true,
            PdfInput = true,
            PdfMaxPages = 600,
            PdfMaxBytes = 96L * 1024 * 1024,
        });
        large.Pricing.Should().Be(new ModelPricing(), "the list carries no prices");
        small.Capabilities.Reasoning.Should().BeFalse("it does not take adaptive thinking");
        small.Capabilities.ReasoningEfforts.Should().BeEmpty();
        small.Capabilities.PdfInput.Should().BeFalse();
        small.Capabilities.PdfMaxPages.Should().BeNull();
        small.Capabilities.ContextTokens.Should().Be(200_000);
    }

    [Theory]
    [InlineData(1_000_000, 600)]
    [InlineData(999_999, 100)]
    [InlineData(null, 100)]
    public async Task A_pdf_reading_model_takes_fewer_pages_below_a_million_tokens_of_context(int? context, int pages)
    {
        var entry = new
        {
            id = "m",
            display_name = "M",
            max_input_tokens = context,
            max_tokens = 1000,
            capabilities = new { pdf_input = new { supported = true } },
        };
        var handler = new ScriptedHandler((_, _, _) =>
            Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = new[] { entry }, has_more = false }))));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));

        var models = await provider.ListModels(Connection, TestKeys.Generate(), Token);

        models.Single().Capabilities.PdfMaxPages.Should().Be(pages);
    }

    [Fact]
    public async Task A_model_with_no_capabilities_claims_only_tools_and_streaming()
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(ScriptedHandler.Json(
            HttpStatusCode.OK,
            """{"data":[{"id":"m","display_name":null,"max_input_tokens":null,"max_tokens":null,"capabilities":null}],"has_more":false}""")));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));

        var model = (await provider.ListModels(Connection, TestKeys.Generate(), Token)).Single();

        model.DisplayName.Should().Be("m");
        model.Capabilities.Should().BeEquivalentTo(new ModelCapabilities());
    }

    #endregion

    #region Paging

    [Fact]
    public async Task Every_page_is_read_after_the_last_id_of_the_one_before()
    {
        List<Uri> asked = [];
        var handler = new ScriptedHandler((request, count, _) =>
        {
            asked.Add(request.RequestUri!);
            var body = count == 1
                ? """{"data":[{"id":"a"},{"id":"b"}],"has_more":true,"first_id":"a","last_id":"b"}"""
                : """{"data":[{"id":"c"}],"has_more":false,"first_id":"c","last_id":"c"}""";
            return Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, body));
        });
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));

        var models = await provider.ListModels(Connection, TestKeys.Generate(), Token);

        models.Select(model => model.Id).Should().Equal("a", "b", "c");
        asked.Select(uri => uri.Query).Should().Equal("?limit=1000", "?limit=1000&after_id=b");
    }

    [Fact]
    public async Task Paging_stops_once_more_models_came_back_than_the_server_accepts()
    {
        var handler = new ScriptedHandler((_, count, _) => Task.FromResult(ScriptedHandler.Json(
            HttpStatusCode.OK,
            $$"""{"data":[{"id":"m{{count}}a"},{"id":"m{{count}}b"}],"has_more":true,"last_id":"m{{count}}b"}""")));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));
        var bounded = Connection with { Limits = new DiscoveryLimits(DiscoveryLimits.Default.MaxBytes, 3) };

        var models = await provider.ListModels(bounded, TestKeys.Generate(), Token);

        handler.Count.Should().Be(2);
        models.Should().HaveCount(4, "the connection service refuses the list once it holds more than the bound");
    }

    #endregion

    #region Helpers

    private static ModelEndpoint Connection { get; } = new(AnthropicProvider.ConnectionKind, AnthropicProvider.BaseAddress);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion
}
