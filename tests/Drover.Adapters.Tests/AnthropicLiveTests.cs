namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the Anthropic adapter against the real API with a key the owner supplies in DROVER_TEST_ANTHROPIC_KEY and a
/// model named in DROVER_TEST_ANTHROPIC_MODEL (no model name is written in code). These tests are explicit and carry the
/// Live trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_ANTHROPIC_KEY=... DROVER_TEST_ANTHROPIC_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*AnthropicLiveTests"</c>.
/// Each one makes a handful of short requests, which cost a fraction of a cent.
/// </summary>
[Trait("Live", "true")]
public sealed class AnthropicLiveTests
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_ANTHROPIC_KEY";

    private const string ModelVariable = "DROVER_TEST_ANTHROPIC_MODEL";

    private static readonly ModelEndpoint connection = new(AnthropicProvider.ConnectionKind, AnthropicProvider.BaseAddress);

    #endregion

    #region Connection

    /// <summary>Needs no key from the owner: a made-up one must come back as the setup message, on test and on a run.</summary>
    [Fact(Explicit = true)]
    public async Task Reports_a_made_up_key_as_invalid()
    {
        var provider = BuildProvider();
        var madeUp = TestKeys.Generate();

        var result = await provider.TestConnection(connection, madeUp, TestContext.Current.CancellationToken);
        var failure = await FluentActions
            .Awaiting(() => provider.CreateChatClient(connection, madeUp, "any-model", new ModelSettings()).Ask("Say hello."))
            .Should().ThrowAsync<ModelProviderException>();

        result.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Which.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Which.Message.Should().NotContain(madeUp);
    }

    [Fact(Explicit = true)]
    public async Task Accepts_the_owners_key_and_lists_the_model_with_what_it_can_do()
    {
        var provider = BuildProvider();

        var result = await provider.TestConnection(connection, ReadKey(), TestContext.Current.CancellationToken);
        var models = await provider.ListModels(connection, ReadKey(), TestContext.Current.CancellationToken);

        result.Should().Be(ConnectionTestResult.Ok("Connected to Anthropic."));
        var model = models.Single(model => model.Id == ReadModel());
        model.Capabilities.MaxOutputTokens.Should().BePositive();
        Note($"{models.Count} models listed; {ReadModel()}: {model.Capabilities}");
    }

    #endregion

    #region Chat

    [Fact(Explicit = true)]
    public async Task Streams_a_short_reply()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { MaxOutputTokens = 2000 });

        var updates = await client.StreamToEnd(ModelProviderConformance.ReasoningPrompt);

        updates.ToChatResponse().Text.Should().NotBeNullOrWhiteSpace();
        Note($"Reasoning words streamed: {updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Sum(part => part.Text.Length)} characters");
    }

    /// <summary>
    /// The questions the fixtures cannot answer: does the provider take its thinking block back as the adapter sends it
    /// after a tool call, and does a second identical request read the cache.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Carries_thinking_across_a_tool_call_and_reads_the_cache_the_second_time()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { MaxOutputTokens = 4000 });
        var tool = new ModelTool(
            "get_weather",
            "Weather for a city",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));
        var instructions = string.Concat(Enumerable.Repeat("You answer questions about the weather, briefly and politely. ", 200));
        var options = new ModelChatOptions { Tools = [tool], Instructions = instructions };
        List<ModelChatMessage> conversation = [new("user", [new ModelText(ModelProviderConformance.WeatherPrompt)])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), TestContext.Current.CancellationToken);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<ModelToolCall>().First();
        conversation.AddRange(first.Messages);
        conversation.Add(new ModelChatMessage("tool", [new ModelToolResult(call.CallId, JsonSerializer.SerializeToElement("Sunny, 21 degrees"))]));
        var second = await client.GetResponse(new ModelChatRequest(conversation, options), TestContext.Current.CancellationToken);

        second.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
        Note($"Signed thinking blocks: {first.Messages.SelectMany(message => message.Contents).OfType<ModelReasoning>().Count(part => part.ProtectedData is not null)}");
        Note($"First usage: {first.Usage}; second usage: {second.Usage}");
        second.Usage!.CachedInputTokens.Should().BePositive("the instructions and tools were cached by the first call");
    }

    [Fact(Explicit = true)]
    public async Task Reads_a_pdf_as_a_pdf()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { MaxOutputTokens = 2000 });
        var pdf = Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
        var request = new ModelChatRequest(
            [new ModelChatMessage("user", [new ModelText("How many pages does this PDF have? Answer with the number."), new ModelData(pdf, "application/pdf")])],
            new ModelChatOptions());

        var response = await client.GetResponse(request, TestContext.Current.CancellationToken);

        response.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
    }

    #endregion

    #region Helpers

    private static AnthropicProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAnthropicProvider();

        return services.BuildServiceProvider().GetRequiredService<AnthropicProvider>();
    }

    private static string ReadKey() =>
        Environment.GetEnvironmentVariable(KeyVariable)
        ?? throw new InvalidOperationException($"Set {KeyVariable} to an Anthropic API key to run the live tests.");

    private static string ReadModel() =>
        Environment.GetEnvironmentVariable(ModelVariable)
        ?? throw new InvalidOperationException($"Set {ModelVariable} to a model the key can use.");

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
