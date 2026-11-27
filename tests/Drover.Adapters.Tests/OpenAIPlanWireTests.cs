namespace Drover.Adapters.Tests;

/// <summary>
/// What a ChatGPT plan connection puts on the wire and how it reads the plan's answers, against a scripted handler and
/// the fixtures under Fixtures/openai-plan. The plan takes only part of the Responses wire: every request streams and
/// stores nothing, carries no system-role item and none of the fields it refuses, and groups function tools in a
/// namespace. Its usage limit, eligibility and availability codes map to Drover's failures, also mid-stream. Shapes
/// from https://developers.openai.com/siwc/llms-full.txt ("Models and inference", "Preview limitations", "Errors and
/// recovery"), read 2026-11-27.
/// </summary>
public sealed class OpenAIPlanWireTests
{
    #region Private Fields

    private const string Model = "gpt-test-reasoner";

    /// <summary>Copied from the preview limitations page ("Unsupported fields"), with the conversation link it also forbids.</summary>
    private static readonly string[] refusedFields =
    [
        "background", "conversation", "max_output_tokens", "max_tool_calls", "metadata", "moderation", "multi_agent",
        "prompt", "prompt_cache_retention", "safety_identifier", "temperature", "top_logprobs", "top_p", "truncation",
        "user", "previous_response_id",
    ];

    private static readonly ModelTool weather = new(
        "get_weather",
        "Weather for a city",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));

    #endregion

    #region Request Shape

    [Fact]
    public async Task Every_request_streams_stores_nothing_and_carries_no_field_the_plan_refuses_whole_and_streamed()
    {
        var wire = new Wire(Stream(TextDeltas("Hi.")), Stream(TextDeltas("Hi.")));
        var client = wire.Client(new ModelSettings { Temperature = 0.3, MaxOutputTokens = 400 });
        var request = Prompt("Say hello.") with { Options = new ModelChatOptions { TopP = 0.5f, Temperature = 0.2f, MaxOutputTokens = 50 } };

        var whole = await client.GetResponse(request, Token);
        await Drain(client.GetStreamingResponse(request, Token));

        Text(whole.Messages.SelectMany(message => message.Contents)).Should().Be("Hi.");
        wire.Requests.Should().HaveCount(2);
        wire.Requests.Should().AllSatisfy(sent =>
        {
            sent.Json.GetProperty("stream").GetBoolean().Should().BeTrue();
            sent.Json.GetProperty("store").GetBoolean().Should().BeFalse();
            Names(sent).Should().NotContain(refusedFields);
        });
    }

    [Fact]
    public async Task The_bots_instructions_go_in_instructions_and_no_item_has_the_system_role()
    {
        var wire = new Wire(Stream(TextDeltas("Hi.")));
        ModelChatMessage system = new("system", [new ModelText("Answer in French.")]);
        var request = new ModelChatRequest([system, new ModelChatMessage("user", [new ModelText("Say hello.")])], new ModelChatOptions { Instructions = "You are Atlas." });

        await wire.Client().GetResponse(request, Token);

        var sent = wire.Requests.Single().Json;
        sent.GetProperty("instructions").GetString().Should().Contain("You are Atlas.").And.Contain("Answer in French.");
        sent.GetProperty("input").EnumerateArray()
            .Where(item => item.TryGetProperty("role", out _))
            .Select(item => item.GetProperty("role").GetString())
            .Should().NotContain("system").And.Contain("user");
    }

    [Fact]
    public async Task Function_tools_are_grouped_in_one_namespace_and_a_call_sent_back_names_it()
    {
        var wire = new Wire(Stream(FunctionCallEvents("call_1")), Stream(TextDeltas("Sunny.")));
        var client = wire.Client();
        var options = new ModelChatOptions { Tools = [weather] };
        List<ModelChatMessage> conversation = [new("user", [new ModelText("What is the weather in Paris?")])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), Token);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<ModelToolCall>().Single();
        conversation.AddRange(first.Messages);
        conversation.Add(new ModelChatMessage("tool", [new ModelToolResult(call.CallId, JsonSerializer.SerializeToElement("Sunny"))]));
        await client.GetResponse(new ModelChatRequest(conversation, options), Token);

        call.Name.Should().Be("get_weather");
        foreach (var sent in wire.Requests)
        {
            var tools = sent.Json.GetProperty("tools").EnumerateArray().ToList();
            var group = tools.Should().ContainSingle().Subject;
            group.GetProperty("type").GetString().Should().Be("namespace");
            group.GetProperty("name").GetString().Should().Be(PlanRequestShaping.ToolNamespace);
            group.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
            group.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Should().Equal("get_weather");
        }

        var callSentBack = wire.Requests[1].Json.GetProperty("input").EnumerateArray()
            .Single(item => item.TryGetProperty("type", out var type) && type.GetString() == "function_call");
        callSentBack.GetProperty("namespace").GetString().Should().Be(PlanRequestShaping.ToolNamespace);
        callSentBack.GetProperty("name").GetString().Should().Be("get_weather");
    }

    #endregion

    #region Destination

    [Fact]
    public async Task Only_the_model_list_and_responses_are_called_on_the_configured_address_with_the_token_as_a_bearer()
    {
        var token = TestKeys.Generate();
        var wire = new Wire(ModelList(), Stream(TextDeltas("Hi.")), Stream(TextDeltas("Hi.")));
        var provider = wire.Provider();
        var connection = new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, new Uri("https://elsewhere.example.test/v1/"));

        await provider.ListModels(connection, token, Token);
        await provider.CreateChatClient(connection, token, Model, new ModelSettings()).GetResponse(Prompt("Say hello."), Token);
        await Drain(provider.CreateChatClient(connection, token, Model, new ModelSettings()).GetStreamingResponse(Prompt("Say hello."), Token));

        wire.Sent.Select(request => $"{request.Method} {request.RequestUri}").Should().Equal(
            "GET https://api.openai.com/v1/models",
            "POST https://api.openai.com/v1/responses",
            "POST https://api.openai.com/v1/responses");
        wire.Sent.Should().AllSatisfy(request => request.Headers.Authorization!.ToString().Should().Be($"Bearer {token}"));
    }

    [Fact]
    public async Task Changing_the_api_address_setting_changes_the_next_request_with_no_rebuild()
    {
        var settings = new SettableOptions<OpenAIPlanOptions>(new());
        var wire = new Wire(Stream(TextDeltas("a")), Stream(TextDeltas("b")));
        var provider = wire.Provider(settings);
        var connection = new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, settings.CurrentValue.ApiAddress);

        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("Say hello."), Token);
        settings.CurrentValue = new OpenAIPlanOptions { ApiAddress = new Uri("https://plan.example.test/v2/") };
        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("Say hello."), Token);

        wire.Sent.Select(request => request.RequestUri!.ToString()).Should().Equal(
            "https://api.openai.com/v1/responses",
            "https://plan.example.test/v2/responses");
        provider.FixedAddress.Should().Be(new Uri("https://plan.example.test/v2/"));
    }

    [Fact]
    public async Task The_model_list_holds_the_entries_meant_for_a_picker_in_the_providers_order_with_no_price()
    {
        var rig = OpenAIPlanRig.Start(ConformanceScenarios.ListModels);

        var models = await rig.Subject.Provider.ListModels(rig.Subject.Connection, rig.Subject.ApiKey, Token);

        models.Select(model => (model.Id, model.DisplayName)).Should().Equal(("gpt-test-reasoner", "GPT Test Reasoner"), ("gpt-test-small", "GPT Test Small"));
        models.Should().AllSatisfy(model =>
        {
            model.Pricing.InputPerMillion.Should().BeNull();
            model.Pricing.OutputPerMillion.Should().BeNull();
            model.Capabilities.ImageGeneration.Should().BeFalse("the plan has no image generation");
            model.Capabilities.WebSearch.Should().BeFalse("the plan has no hosted tools Drover would use");
            model.Capabilities.SpeechToText.Should().BeFalse("the plan takes no audio input");
            model.Capabilities.TextToSpeech.Should().BeFalse();
            model.Capabilities.RealtimeVoice.Should().BeFalse();
            model.Capabilities.ToolCalling.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Testing_without_a_token_asks_for_a_sign_in_and_sends_nothing()
    {
        var wire = new Wire();
        var provider = wire.Provider();

        var result = await provider.TestConnection(new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, new OpenAIPlanOptions().ApiAddress), null, Token);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ModelFailure.InvalidKey);
        wire.Sent.Should().BeEmpty();
    }

    #endregion

    #region Failures

    [Theory]
    [InlineData("plan_usage_limit", ModelFailure.OutOfCredit, PlanFailures.LimitReached)]
    [InlineData("plan_not_eligible", ModelFailure.Rejected, PlanFailures.NotEligible)]
    [InlineData("plan_usage_unavailable", ModelFailure.RetryLater, PlanFailures.Unavailable)]
    [InlineData("plan_user_unavailable", ModelFailure.RetryLater, PlanFailures.Unavailable)]
    [InlineData("plan_unsupported_capability", ModelFailure.Rejected, PlanFailures.UnsupportedRequest)]
    [InlineData("plan_usage_limit_in_stream", ModelFailure.OutOfCredit, PlanFailures.LimitReached)]
    [InlineData("plan_usage_unavailable_in_stream", ModelFailure.RetryLater, PlanFailures.Unavailable)]
    public async Task Each_plan_code_is_its_own_failure_whole_and_streamed(string fixture, ModelFailure expected, string sentence)
    {
        var whole = OpenAIPlanRig.Start(fixture).Subject;
        var streamed = OpenAIPlanRig.Start(fixture).Subject;

        var wholeFailure = (await FluentActions.Awaiting(() => whole.CreateChatClient().GetResponse(Prompt("Say hello."), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;
        var streamedFailure = (await FluentActions.Awaiting(() => Drain(streamed.CreateChatClient().GetStreamingResponse(Prompt("Say hello."), Token)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        foreach (var failure in (ModelProviderException[])[wholeFailure, streamedFailure])
        {
            failure.Failure.Should().Be(expected);
            failure.Message.Should().Be(sentence);
            failure.Message.Should().NotContain("Made-up message", "the provider's own words are never repeated");
            failure.RetryAfter.Should().BeNull("the provider says not to infer a reset time");
            failure.InnerException.Should().BeNull("the vendor's exception stays inside the adapter");
        }
    }

    [Fact]
    public async Task A_plan_limit_is_not_sent_again_but_a_call_that_never_reached_the_server_is()
    {
        var limited = new ScriptedHandler((_, _, _) => Task.FromResult(ScriptedHandler.Json(
            HttpStatusCode.TooManyRequests,
            """{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"m","type":"t","param":null}}""")));
        var refusedOnce = new ScriptedHandler((_, attempt, _) => attempt == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : Task.FromResult(EventStream(TextDeltas("Hi."))));

        var limitFailure = await FluentActions.Awaiting(() => Registered(limited).GetResponse(Prompt("Say hello."), Token)).Should().ThrowAsync<ModelProviderException>();
        var answer = await Registered(refusedOnce).GetResponse(Prompt("Say hello."), Token);

        limitFailure.Which.Failure.Should().Be(ModelFailure.OutOfCredit);
        limited.Count.Should().Be(1, "the plan's limit is the person's; the provider asks apps to pause, not to repeat");
        Text(answer.Messages.SelectMany(message => message.Contents)).Should().Be("Hi.");
        refusedOnce.Count.Should().Be(2);
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static List<string> Names(WireRequest request) => [.. request.Json.EnumerateObject().Select(property => property.Name)];

    private static string Text(IEnumerable<ModelContent> contents) => string.Concat(contents.OfType<ModelText>().Select(content => content.Text));

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    /// <summary>A plan client built by the adapter's own registration, with its pipeline, over <paramref name="handler"/>.</summary>
    private static IModelChatClient Registered(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddOpenAIPlanProvider();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient(OpenAIPlanProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.Configure<HttpStandardResilienceOptions>($"{OpenAIPlanProvider.HttpClientName}-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });
        var provider = services.BuildServiceProvider().GetRequiredService<OpenAIPlanProvider>();
        return provider.CreateChatClient(new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, new OpenAIPlanOptions().ApiAddress), TestKeys.Generate(), Model, new ModelSettings());
    }

    private static object Response(string status, object[]? output = null) => new
    {
        id = "resp_plan_wire",
        @object = "response",
        created_at = 1794400000,
        status,
        error = (object?)null,
        incomplete_details = (object?)null,
        model = Model,
        output = output ?? [],
        usage = new { input_tokens = 5, output_tokens = 2, total_tokens = 7 },
    };

    private static string Event(string type, object fields)
    {
        var json = JsonSerializer.SerializeToNode(fields)!.AsObject();
        json["type"] = type;
        json["sequence_number"] = 0;
        return json.ToJsonString();
    }

    private static IEnumerable<string> TextDeltas(string text)
    {
        object message = new { type = "message", id = "msg_plan", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } };
        yield return Event("response.created", new { response = Response("in_progress") });
        yield return Event("response.output_text.delta", new { item_id = "msg_plan", output_index = 0, content_index = 0, delta = text });
        yield return Event("response.output_item.done", new { output_index = 0, item = message });
        yield return Event("response.completed", new { response = Response("completed", [message]) });
    }

    private static IEnumerable<string> FunctionCallEvents(string callId)
    {
        object call = new { type = "function_call", id = "fc_plan", call_id = callId, name = "get_weather", @namespace = PlanRequestShaping.ToolNamespace, arguments = """{"city":"Paris"}""", status = "completed" };
        yield return Event("response.created", new { response = Response("in_progress") });
        yield return Event("response.output_item.done", new { output_index = 0, item = call });
        yield return Event("response.completed", new { response = Response("completed", [call]) });
    }

    private static Answer Stream(IEnumerable<string> events) =>
        new(HttpStatusCode.OK, "text/event-stream", string.Concat(events.Select(@event => $"data: {@event}\n\n")));

    private static HttpResponseMessage EventStream(IEnumerable<string> events)
    {
        var answer = Stream(events);
        var content = new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(answer.Body))));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(answer.ContentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static Answer ModelList() => new(
        HttpStatusCode.OK,
        "application/json",
        """{"models":[{"slug":"gpt-test-reasoner","display_name":"GPT Test Reasoner","visibility":"list"}]}""");

    private sealed record Answer(HttpStatusCode Status, string ContentType, string Body);

    /// <summary>Answers each request with the next scripted answer, in order, and keeps what was sent.</summary>
    private sealed class Wire
    {
        #region Private Fields

        private readonly Answer[] answers;

        #endregion

        #region Constructors

        public Wire(params Answer[] answers)
        {
            this.answers = answers;
            Handler = new ScriptedHandler(Answer);
        }

        #endregion

        #region Properties

        public ScriptedHandler Handler { get; }

        public List<HttpRequestMessage> Sent { get; } = [];

        public List<WireRequest> Requests { get; } = [];

        #endregion

        #region Public Methods

        public OpenAIPlanProvider Provider(IOptionsMonitor<OpenAIPlanOptions>? settings = null) =>
            new(new FixtureHttpClientFactory(Handler), settings ?? new SettableOptions<OpenAIPlanOptions>(new()));

        public IModelChatClient Client(ModelSettings? settings = null) =>
            Provider().CreateChatClient(new ModelEndpoint(OpenAIPlanProvider.ConnectionKind, new OpenAIPlanOptions().ApiAddress), TestKeys.Generate(), Model, settings ?? new ModelSettings());

        #endregion

        #region Private Methods

        private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, int count, CancellationToken cancellation)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
            Sent.Add(request);
            Requests.Add(new WireRequest(request.Method.Method, request.RequestUri!, body));
            var answer = answers[count - 1];
            HttpContent content = answer.ContentType == "text/event-stream"
                ? new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(answer.Body))))
                : new StringContent(answer.Body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(answer.ContentType);

            return new HttpResponseMessage(answer.Status) { Content = content };
        }

        #endregion
    }

    #endregion
}
