namespace Drover.Adapters.Conformance;

/// <summary>
/// The suite every <see cref="IModelClientProvider"/> must pass. An adapter's test class derives from this,
/// says which optional features it claims, and serves canned responses for each scenario.
/// A new behavior every adapter must have is added here first.
/// </summary>
public abstract class ModelProviderConformance
{
    #region Constants

    public const string GreetingPrompt = "Say hello.";

    public const string WeatherPrompt = "What is the weather in Paris?";

    public const string StructuredPrompt = "Report the weather in Paris.";

    public const string ReasoningPrompt = "What is 15 percent of 240?";

    #endregion

    #region Adapter Hooks

    protected abstract ConformanceClaims Claims { get; }

    protected abstract ProviderUnderTest Start(string scenario);

    #endregion

    #region Model Listing

    [Fact]
    public async Task Lists_the_models_the_connection_offers()
    {
        var subject = Start(ConformanceScenarios.ListModels);

        var models = await subject.Provider.ListModels(subject.Connection, subject.ApiKey, CancellationToken.None);

        models.Should().NotBeEmpty();
        models.Should().OnlyContain(model => model.Id != string.Empty && model.DisplayName != string.Empty);
        models.Select(model => model.Id).Should().OnlyHaveUniqueItems().And.Contain(subject.ModelId);
    }

    [Fact]
    public async Task Claims_optional_features_only_where_the_model_record_says_so()
    {
        var subject = Start(ConformanceScenarios.ListModels);

        var models = await subject.Provider.ListModels(subject.Connection, subject.ApiKey, CancellationToken.None);
        var capabilities = models.Single(model => model.Id == subject.ModelId).Capabilities;

        capabilities.StructuredOutput.Should().Be(Claims.StructuredOutput);
        capabilities.Reasoning.Should().Be(Claims.ReasoningText);
    }

    [Fact]
    public async Task Test_connection_succeeds_with_a_working_key()
    {
        var subject = Start(ConformanceScenarios.TestConnection);

        var result = await subject.Provider.TestConnection(subject.Connection, subject.ApiKey, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Message);
        result.Failure.Should().BeNull();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Chat

    [Fact]
    public async Task Answers_a_plain_message()
    {
        var subject = Start(ConformanceScenarios.PlainReply);

        var response = await subject.CreateChatClient().GetResponseAsync(GreetingPrompt);

        response.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Streams_a_reply_in_several_parts()
    {
        var subject = Start(ConformanceScenarios.Streaming);

        List<ChatResponseUpdate> updates = [];

        await foreach (var update in subject.CreateChatClient().GetStreamingResponseAsync(GreetingPrompt))
        {
            updates.Add(update);
        }

        updates.Count(update => !string.IsNullOrEmpty(update.Text)).Should().BeGreaterThan(1);
        updates.ToChatResponse().Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Completes_a_tool_call_round_trip()
    {
        var subject = Start(ConformanceScenarios.ToolCall);
        var client = subject.CreateChatClient();
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather, "get_weather")] };
        List<ChatMessage> conversation = [new(ChatRole.User, WeatherPrompt)];

        var first = await client.GetResponseAsync(conversation, options);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();

        call.Name.Should().Be("get_weather");
        call.Arguments.Should().ContainKey("city");
        call.Arguments!["city"]!.ToString().Should().Be("Paris");

        conversation.AddRange(first.Messages);
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "Sunny, 21 degrees")]));

        var second = await client.GetResponseAsync(conversation, options);

        second.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Returns_structured_output_where_claimed()
    {
        if (!Claims.StructuredOutput)
        {
            return;
        }

        var subject = Start(ConformanceScenarios.StructuredOutput);
        var schema = AIJsonUtilities.CreateJsonSchema(typeof(WeatherReport));
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "weather_report") };

        var response = await subject.CreateChatClient().GetResponseAsync(StructuredPrompt, options);
        var report = JsonSerializer.Deserialize<WeatherReport>(response.Text, JsonSerializerOptions.Web);

        report.Should().NotBeNull();
        report!.City.Should().Be("Paris");
    }

    [Fact]
    public async Task Returns_reasoning_text_where_claimed()
    {
        if (!Claims.ReasoningText)
        {
            return;
        }

        var subject = Start(ConformanceScenarios.ReasoningText);
        var client = subject.CreateChatClient();

        var whole = await client.GetResponseAsync(ReasoningPrompt);

        ReasoningText(whole.Messages.SelectMany(message => message.Contents)).Should().NotBeNullOrWhiteSpace();
        whole.Text.Should().NotBeNullOrWhiteSpace();

        List<ChatResponseUpdate> updates = [];

        await foreach (var update in client.GetStreamingResponseAsync(ReasoningPrompt))
        {
            updates.Add(update);
        }

        ReasoningText(updates.SelectMany(update => update.Contents)).Should().NotBeNullOrWhiteSpace();
        updates.ToChatResponse().Text.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Failures

    [Fact]
    public async Task Maps_a_rate_limit_to_retry_later()
    {
        var subject = Start(ConformanceScenarios.RateLimited);

        var failure = (await FluentActions
            .Awaiting(() => subject.CreateChatClient().GetResponseAsync(GreetingPrompt))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
        failure.RetryAfter.Should().NotBeNull();
        failure.Message.Should().NotContain(subject.ApiKey);
    }

    [Fact]
    public async Task Maps_a_rate_limit_while_streaming_to_retry_later()
    {
        var subject = Start(ConformanceScenarios.RateLimited);

        var failure = (await FluentActions
            .Awaiting(() => Drain(subject.CreateChatClient().GetStreamingResponseAsync(GreetingPrompt)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
    }

    [Fact]
    public async Task Maps_an_invalid_key_to_a_setup_message()
    {
        var subject = Start(ConformanceScenarios.InvalidKey);

        var failure = (await FluentActions
            .Awaiting(() => subject.CreateChatClient().GetResponseAsync(GreetingPrompt))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Message.Should().Contain("key");
        failure.Message.Should().NotContain(subject.ApiKey);
    }

    [Fact]
    public async Task Reports_an_invalid_key_when_testing_the_connection()
    {
        var subject = Start(ConformanceScenarios.InvalidKey);

        var result = await subject.Provider.TestConnection(subject.Connection, subject.ApiKey, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ModelFailure.InvalidKey);
        result.Message.Should().Contain("key").And.NotContain(subject.ApiKey);
    }

    #endregion

    #region Helpers

    private static async Task Drain(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (var update in updates)
        {
            update.Contents.Should().NotBeNull();
        }
    }

    private static string ReasoningText(IEnumerable<AIContent> contents) =>
        string.Concat(contents.OfType<TextReasoningContent>().Select(reasoning => reasoning.Text));

    private static string GetWeather([Description("The city")] string city) => $"Sunny in {city}";

    private sealed record WeatherReport(string City, string Summary);

    #endregion
}
