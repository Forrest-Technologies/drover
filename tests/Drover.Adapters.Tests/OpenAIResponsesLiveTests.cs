namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the OpenAI adapter against the real Responses API with a key the owner supplies in DROVER_TEST_OPENAI_KEY and a
/// reasoning model named in DROVER_TEST_OPENAI_MODEL (no model name is written in code). These tests are explicit and
/// carry the Live trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_OPENAI_KEY=... DROVER_TEST_OPENAI_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*OpenAIResponsesLiveTests"</c>.
/// Each one makes a handful of short requests, which cost a fraction of a cent.
/// </summary>
[Trait("Live", "true")]
public sealed class OpenAIResponsesLiveTests
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_OPENAI_KEY";

    private const string ModelVariable = "DROVER_TEST_OPENAI_MODEL";

    private const string PlainModelVariable = "DROVER_TEST_OPENAI_PLAIN_MODEL";

    private static readonly ModelEndpoint connection = new(OpenAIProvider.ConnectionKind, OpenAIProvider.BaseAddress);

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
    public async Task Accepts_the_owners_key_and_lists_the_model()
    {
        var provider = BuildProvider();

        var result = await provider.TestConnection(connection, ReadKey(), TestContext.Current.CancellationToken);
        var models = await provider.ListModels(connection, ReadKey(), TestContext.Current.CancellationToken);

        result.Should().Be(ConnectionTestResult.Ok("Connected to OpenAI."));
        models.Select(model => model.Id).Should().Contain(ReadModel());
        Note($"{models.Count} models listed");
    }

    #endregion

    #region Chat

    /// <summary>
    /// The question the fixtures cannot answer: with <c>store: false</c>, does the provider take its encrypted reasoning
    /// back as the adapter sends it (no item id), and answer the tool result after it.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Carries_reasoning_across_a_tool_call_with_nothing_stored()
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

        var sealedReasoning = first.Messages.SelectMany(message => message.Contents).OfType<ModelReasoning>().Count(reasoning => reasoning.ProtectedData is not null);
        Note($"Sealed reasoning items: {sealedReasoning}; first usage: {first.Usage}; second usage: {second.Usage}");
        second.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
    }

    [Fact(Explicit = true)]
    public async Task Streams_a_reply_with_reasoning_words_and_usage()
    {
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), ReadModel(), new ModelSettings { ReasoningEffort = "medium" });

        var updates = await client.StreamToEnd(ModelProviderConformance.ReasoningPrompt);

        updates.ToChatResponse().Text.Should().NotBeNullOrWhiteSpace();
        Note($"Reasoning words streamed: {updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Sum(part => part.Text.Length)} characters");
        Note($"Usage: {updates.SelectMany(update => update.Contents).OfType<UsageContent>().LastOrDefault()?.Details.CachedInputTokenCount} cached");
    }

    /// <summary>
    /// A model that does not reason, named in DROVER_TEST_OPENAI_PLAIN_MODEL: the summary every call asks for is either
    /// ignored or refused, and a refusal is answered by the same call without it.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Answers_on_a_model_that_does_not_reason()
    {
        var model = Environment.GetEnvironmentVariable(PlainModelVariable)
            ?? throw new InvalidOperationException($"Set {PlainModelVariable} to a model that does not reason.");
        var client = BuildProvider().CreateChatClient(connection, ReadKey(), model, new ModelSettings());

        var response = await client.GetResponse(
            new ModelChatRequest([new ModelChatMessage("user", [new ModelText("Reply with the single word: ready")])], new ModelChatOptions()),
            TestContext.Current.CancellationToken);

        response.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Should().NotBeEmpty();
    }

    #endregion

    #region Helpers

    private static OpenAIProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddOpenAIProvider();

        return services.BuildServiceProvider().GetRequiredService<OpenAIProvider>();
    }

    private static string ReadKey() =>
        Environment.GetEnvironmentVariable(KeyVariable)
        ?? throw new InvalidOperationException($"Set {KeyVariable} to an OpenAI API key to run the live tests.");

    private static string ReadModel() =>
        Environment.GetEnvironmentVariable(ModelVariable)
        ?? throw new InvalidOperationException($"Set {ModelVariable} to a reasoning model the key can use.");

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
