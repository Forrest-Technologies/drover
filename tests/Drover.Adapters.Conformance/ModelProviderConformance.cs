namespace Drover.Adapters.Conformance;

/// <summary>
/// The suite every <see cref="IModelClientProvider"/> must pass. An adapter's test class derives from this,
/// says which optional features it claims, and serves canned responses for each scenario.
/// A new behavior every adapter must have is added here first.
/// </summary>
public abstract class ModelProviderConformance
{
    #region Private Fields

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

        var response = await subject.CreateChatClient().GetResponse(Prompt(GreetingPrompt));

        Text(response.Messages.SelectMany(message => message.Contents)).Should().NotBeNullOrWhiteSpace();
        VendorTypesIn(response).Should().BeEmpty("no vendor object may leave the adapter");
    }

    [Fact]
    public async Task Streams_a_reply_in_several_parts()
    {
        var subject = Start(ConformanceScenarios.Streaming);

        List<ModelChatUpdate> updates = [];

        await foreach (var update in subject.CreateChatClient().GetStreamingResponse(Prompt(GreetingPrompt)))
        {
            updates.Add(update);
        }

        updates.Count(update => !string.IsNullOrEmpty(Text(update.Contents))).Should().BeGreaterThan(1);
        Text(updates.SelectMany(update => update.Contents)).Should().NotBeNullOrWhiteSpace();
        updates.SelectMany(update => VendorTypesIn(update)).Should().BeEmpty("no vendor object may leave the adapter");
    }

    [Fact]
    public async Task Completes_a_tool_call_round_trip()
    {
        var subject = Start(ConformanceScenarios.ToolCall);
        var client = subject.CreateChatClient();
        var options = new ModelChatOptions { Tools = [new ModelTool("get_weather", "Weather for a city", JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }))] };
        List<ModelChatMessage> conversation = [new("user", [new ModelText(WeatherPrompt)])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options));
        var call = first.Messages.SelectMany(message => message.Contents).OfType<ModelToolCall>().Single();

        call.Name.Should().Be("get_weather");
        call.Arguments.TryGetProperty("city", out _).Should().BeTrue();
        call.Arguments.GetProperty("city").GetString().Should().Be("Paris");

        conversation.AddRange(first.Messages);
        conversation.Add(new ModelChatMessage("tool", [new ModelToolResult(call.CallId, JsonSerializer.SerializeToElement("Sunny, 21 degrees"))]));

        var second = await client.GetResponse(new ModelChatRequest(conversation, options));

        Text(second.Messages.SelectMany(message => message.Contents)).Should().NotBeNullOrWhiteSpace();

        var chats = ChatRequests(subject);
        chats.Should().HaveCount(2);
        chats[0].Json.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("function").GetProperty("name").GetString())
            .Should().Contain("get_weather");

        var sentBack = chats[1].Json.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "tool");
        sentBack.GetProperty("tool_call_id").GetString().Should().Be(call.CallId);
        sentBack.GetProperty("content").GetString().Should().Be("Sunny, 21 degrees");
    }

    [Fact]
    public async Task Returns_structured_output_where_claimed()
    {
        if (!Claims.StructuredOutput)
        {
            return;
        }

        var subject = Start(ConformanceScenarios.StructuredOutput);
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" }, summary = new { type = "string" } } });
        var options = new ModelChatOptions { ResponseFormat = "json", ResponseSchema = schema, ResponseSchemaName = "weather_report" };

        var response = await subject.CreateChatClient().GetResponse(Prompt(StructuredPrompt) with { Options = options });
        var report = JsonSerializer.Deserialize<WeatherReport>(Text(response.Messages.SelectMany(message => message.Contents)), JsonSerializerOptions.Web);

        report.Should().NotBeNull();
        report!.City.Should().Be("Paris");

        var format = ChatRequests(subject).Single().Json.GetProperty("response_format");
        format.GetProperty("type").GetString().Should().Be("json_schema");
        format.GetProperty("json_schema").GetProperty("name").GetString().Should().Be("weather_report");
    }

    [Fact]
    public async Task A_bot_set_to_structured_output_asks_for_a_json_object_and_any_other_bot_does_not()
    {
        var plain = Start(ConformanceScenarios.PlainReply);
        var structured = Start(ConformanceScenarios.PlainReply);
        var settings = new ModelSettings { StructuredOutput = true };

        await plain.CreateChatClient().GetResponse(Prompt(GreetingPrompt));
        await structured.Provider.CreateChatClient(structured.Connection, structured.ApiKey, structured.ModelId, settings).GetResponse(Prompt(GreetingPrompt));

        ChatRequests(plain).Single().Json.TryGetProperty("response_format", out _).Should().BeFalse("a bot that was not set to it sends no response format");
        ChatRequests(structured).Single().Json.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
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

        var whole = await client.GetResponse(Prompt(ReasoningPrompt));

        ReasoningText(whole.Messages.SelectMany(message => message.Contents)).Should().NotBeNullOrWhiteSpace();
        Text(whole.Messages.SelectMany(message => message.Contents)).Should().NotBeNullOrWhiteSpace();

        List<ModelChatUpdate> updates = [];

        await foreach (var update in client.GetStreamingResponse(Prompt(ReasoningPrompt)))
        {
            updates.Add(update);
        }

        ReasoningText(updates.SelectMany(update => update.Contents)).Should().NotBeNullOrWhiteSpace();
        Text(updates.SelectMany(update => update.Contents)).Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Failures

    [Fact]
    public async Task Maps_a_rate_limit_to_retry_later()
    {
        var subject = Start(ConformanceScenarios.RateLimited);

        var failure = (await FluentActions
            .Awaiting(() => subject.CreateChatClient().GetResponse(Prompt(GreetingPrompt)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
        failure.RetryAfter.Should().NotBeNull();
        failure.Message.Should().NotContain(subject.ApiKey);
        failure.InnerException.Should().BeNull("the vendor's exception stays inside the adapter");
    }

    [Fact]
    public async Task Maps_a_rate_limit_while_streaming_to_retry_later()
    {
        var subject = Start(ConformanceScenarios.RateLimited);

        var failure = (await FluentActions
            .Awaiting(() => Drain(subject.CreateChatClient().GetStreamingResponse(Prompt(GreetingPrompt))))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
    }

    [Fact]
    public async Task Maps_an_invalid_key_to_a_setup_message()
    {
        var subject = Start(ConformanceScenarios.InvalidKey);

        var failure = (await FluentActions
            .Awaiting(() => subject.CreateChatClient().GetResponse(Prompt(GreetingPrompt)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.Message.Should().Contain("key");
        failure.Message.Should().NotContain(subject.ApiKey);
        failure.InnerException.Should().BeNull("the vendor's exception stays inside the adapter");
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

    private static List<WireRequest> ChatRequests(ProviderUnderTest subject) =>
        [.. subject.SentRequests().Where(request => request.Uri.AbsolutePath.EndsWith("/chat/completions"))];

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static string Text(IEnumerable<ModelContent> contents) => string.Concat(contents.OfType<ModelText>().Select(content => content.Text));

    /// <summary>Inspects the owned content graph, not just a vendor's optional raw-object slot.</summary>
    private static IEnumerable<object> VendorTypesIn(ModelChatResponse response) =>
        response.Messages.SelectMany(message => message.Contents).Where(IsVendor);

    private static IEnumerable<object> VendorTypesIn(ModelChatUpdate update) => update.Contents.Where(IsVendor);

    private static bool IsVendor(object value) => value.GetType().Assembly != typeof(ModelContent).Assembly;

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        await foreach (var update in updates)
        {
            update.Contents.Should().NotBeNull();
        }
    }

    private static string ReasoningText(IEnumerable<ModelContent> contents) =>
        string.Concat(contents.OfType<ModelReasoning>().Select(reasoning => reasoning.Text));

    private sealed record WeatherReport(string City, string Summary);

    #endregion
}
