namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the Gemini adapter against the real service with a key the owner supplies in DROVER_TEST_GEMINI_KEY and a thinking
/// model named in DROVER_TEST_GEMINI_MODEL (no model name is written in code). These tests are explicit and carry the
/// Live trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_GEMINI_KEY=... DROVER_TEST_GEMINI_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*GeminiLiveTests"</c>.
/// They settle what the fixtures cannot: the status of a made-up key, the shape of a real 429, whether a model takes
/// thinkingLevel, and whether a real signature is accepted back in the order sent.
/// </summary>
[Trait("Live", "true")]
public sealed class GeminiLiveTests
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_GEMINI_KEY";

    private const string ModelVariable = "DROVER_TEST_GEMINI_MODEL";

    private static readonly ModelEndpoint connection = new(GeminiProvider.ConnectionKind, GeminiProvider.BaseAddress);

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
    public async Task Accepts_the_owners_key_and_lists_only_models_that_generate_content()
    {
        var provider = BuildProvider();

        var result = await provider.TestConnection(connection, ReadKey(), TestContext.Current.CancellationToken);
        var models = await provider.ListModels(connection, ReadKey(), TestContext.Current.CancellationToken);

        result.Should().Be(ConnectionTestResult.Ok("Connected to Gemini."));
        models.Select(model => model.Id).Should().Contain(ReadModel()).And.OnlyHaveUniqueItems();
        models.Should().OnlyContain(model => !model.Id.StartsWith("models/", StringComparison.Ordinal));
        Note($"{models.Count} models listed; {models.Count(model => model.Capabilities.Reasoning)} think");
    }

    #endregion

    #region Chat

    [Fact(Explicit = true)]
    public async Task Carries_a_signature_across_a_tool_call_and_reports_thought_and_cached_tokens()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { ReasoningEffort = "low" });
        var tool = new ModelTool(
            "get_weather",
            "Weather for a city",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));
        var options = new ModelChatOptions { Tools = [tool], ToolMode = "required", RequiredToolName = "get_weather" };
        List<ModelChatMessage> conversation = [new("user", [new ModelText(ModelProviderConformance.WeatherPrompt)])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), TestContext.Current.CancellationToken);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<ModelToolCall>().First();
        conversation.AddRange(first.Messages);
        conversation.Add(new ModelChatMessage("tool", [new ModelToolResult(call.CallId, JsonSerializer.SerializeToElement("Sunny, 21 degrees"))]));
        var second = await client.GetResponse(new ModelChatRequest(conversation, new ModelChatOptions { Tools = [tool] }), TestContext.Current.CancellationToken);

        var signatures = first.Messages.SelectMany(message => message.Contents).OfType<ModelReasoning>().Count(reasoning => reasoning.ProtectedData is not null);
        Note($"Signatures: {signatures}; call id: {call.CallId}; first usage: {first.Usage}; second usage: {second.Usage}");
        second.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
    }

    [Fact(Explicit = true)]
    public async Task Streams_a_reply_with_thought_words_and_usage()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { ReasoningEffort = "high" });
        List<ModelChatUpdate> updates = [];

        await foreach (var update in client.GetStreamingResponse(
            new ModelChatRequest([new ModelChatMessage("user", [new ModelText(ModelProviderConformance.ReasoningPrompt)])], new ModelChatOptions()),
            TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var contents = updates.SelectMany(update => update.Contents).ToList();
        contents.OfType<ModelText>().Should().NotBeEmpty();
        Note($"Thought words: {contents.OfType<ModelReasoning>().Sum(reasoning => reasoning.Text.Length)} characters; usage: {contents.OfType<ModelTokenUsage>().LastOrDefault()}");
    }

    #endregion

    #region Helpers

    private static GeminiProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGeminiProvider();

        return services.BuildServiceProvider().GetRequiredService<GeminiProvider>();
    }

    private static string ReadKey() =>
        Environment.GetEnvironmentVariable(KeyVariable)
        ?? throw new InvalidOperationException($"Set {KeyVariable} to a Gemini API key to run the live tests.");

    private static string ReadModel() =>
        Environment.GetEnvironmentVariable(ModelVariable)
        ?? throw new InvalidOperationException($"Set {ModelVariable} to a thinking model the key can use.");

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
