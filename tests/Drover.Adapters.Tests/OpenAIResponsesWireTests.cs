namespace Drover.Adapters.Tests;

/// <summary>
/// What the Responses client puts on the wire and how it reads the answer, against a scripted handler. The vendor's
/// chat client sends no <c>store</c> of its own (the provider's default is to store), names the reasoning item's
/// encrypted content as <c>ProtectedData</c> and sends it back, and reports a refusal and the end of a cut or failed
/// stream in ways Drover must turn into its own terms; these tests pin all of it. Shapes from
/// https://developers.openai.com/api/reference/resources/responses/methods/create and the guides on reasoning,
/// function calling, structured outputs and streaming under https://developers.openai.com/api/docs/guides/ (read
/// 2026-11-26).
/// </summary>
public sealed class OpenAIResponsesWireTests
{
    #region Private Fields

    private const string Model = "gpt-test-reasoner";

    private static readonly ModelEndpoint connection = new(OpenAIProvider.ConnectionKind, OpenAIProvider.BaseAddress);

    private static readonly ModelTool weather = new(
        "get_weather",
        "Weather for a city",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));

    #endregion

    #region Request Shape

    [Fact]
    public async Task Every_request_says_store_false_and_names_no_earlier_response()
    {
        var wire = new Wire(Reply(Message("Hi.")), Stream(TextDeltas("Hi", ".")), Reply(Message("Again.")));
        var client = wire.Client();

        await client.GetResponse(Prompt("Say hello."), Token);
        await Drain(client.GetStreamingResponse(Prompt("Say hello."), Token));
        await client.GetResponse(Prompt("Say hello."), Token);

        wire.Requests.Should().HaveCount(3);
        wire.Requests.Select(request => request.Json.GetProperty("store").GetBoolean()).Should().AllBeEquivalentTo(false);
        wire.Requests.Select(request => Names(request)).Should().AllSatisfy(names =>
            names.Should().NotContain(["previous_response_id", "conversation"], "Drover sends the whole history every time"));
    }

    [Fact]
    public async Task Asks_for_a_reasoning_summary_and_sends_the_effort_only_when_one_is_set()
    {
        var wire = new Wire(Reply(Message("a")), Reply(Message("b")), Reply(Message("c")));

        await wire.Client().GetResponse(Prompt("Say hello."), Token);
        await wire.Client().GetResponse(Prompt("Say hello.") with { Options = new ModelChatOptions { ReasoningEffort = "xhigh" } }, Token);
        await wire.Client(new ModelSettings { ReasoningEffort = "minimal" }).GetResponse(Prompt("Say hello."), Token);

        var reasoning = wire.Requests.Select(request => request.Json.GetProperty("reasoning")).ToList();
        reasoning.Should().OnlyContain(value => value.GetProperty("summary").GetString() == "auto");
        reasoning[0].TryGetProperty("effort", out _).Should().BeFalse("a bot with no effort leaves it to the model");
        reasoning[1].GetProperty("effort").GetString().Should().Be("xhigh");
        reasoning[2].GetProperty("effort").GetString().Should().Be("minimal", "the bot's own setting is the default");
        wire.Requests.Select(request => Names(request)).Should().AllSatisfy(names => names.Should().NotContain(
            "include",
            "a request with store false gets encrypted reasoning by default, and a model that does not reason refuses the include"));
    }

    [Fact]
    public async Task Sends_the_output_limit_the_tool_switches_and_the_instructions_where_they_belong()
    {
        var wire = new Wire(Reply(Message("ok")));
        var options = new ModelChatOptions
        {
            Instructions = "Be brief.",
            MaxOutputTokens = 300,
            AllowMultipleToolCalls = false,
            Tools = [weather],
        };

        await wire.Client().GetResponse(Prompt("Say hello.") with { Options = options }, Token);

        var sent = wire.Requests.Single().Json;
        sent.GetProperty("model").GetString().Should().Be(Model);
        sent.GetProperty("instructions").GetString().Should().Be("Be brief.");
        sent.GetProperty("max_output_tokens").GetInt32().Should().Be(300);
        sent.GetProperty("parallel_tool_calls").GetBoolean().Should().BeFalse();
        var tool = sent.GetProperty("tools").EnumerateArray().Single();
        tool.GetProperty("type").GetString().Should().Be("function");
        tool.GetProperty("name").GetString().Should().Be("get_weather");
        tool.TryGetProperty("function", out _).Should().BeFalse("a Responses function tool is flat");
        var input = sent.GetProperty("input").EnumerateArray().Single();
        input.GetProperty("role").GetString().Should().Be("user");
        input.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
    }

    [Fact]
    public async Task A_schema_goes_as_text_format_and_the_bots_json_switch_as_a_json_object()
    {
        var wire = new Wire(Reply(Message("{}")), Reply(Message("{}")));
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } });

        await wire.Client().GetResponse(Prompt("Report.") with { Options = new ModelChatOptions { ResponseSchema = schema, ResponseSchemaName = "weather_report" } }, Token);
        await wire.Client(new ModelSettings { StructuredOutput = true }).GetResponse(Prompt("Report."), Token);

        var named = wire.Requests[0].Json.GetProperty("text").GetProperty("format");
        named.GetProperty("type").GetString().Should().Be("json_schema");
        named.GetProperty("name").GetString().Should().Be("weather_report");
        named.GetProperty("schema").GetProperty("properties").TryGetProperty("city", out _).Should().BeTrue();
        wire.Requests[1].Json.GetProperty("text").GetProperty("format").GetProperty("type").GetString().Should().Be("json_object");
    }

    [Fact]
    public async Task A_picture_goes_as_an_image_input_part()
    {
        var wire = new Wire(Reply(Message("A red square.")));
        var picture = new ModelChatMessage("user", [new ModelText("What is this?"), new ModelData(new byte[] { 137, 80, 78, 71 }, "image/png")]);

        await wire.Client().GetResponse(new ModelChatRequest([picture], new ModelChatOptions()), Token);

        var parts = wire.Requests.Single().Json.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToList();
        parts.Select(part => part.GetProperty("type").GetString()).Should().Equal("input_text", "input_image");
        parts[1].GetProperty("image_url").GetString().Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_is_refused_in_words_before_anything_is_sent()
    {
        var wire = new Wire(Reply(Message("unused")));
        var file = new ModelChatMessage("user", [new ModelText("Read this."), new ModelData(new byte[] { 37, 80, 68, 70 }, "application/pdf")]);

        var failure = (await FluentActions.Awaiting(() => wire.Client().GetResponse(new ModelChatRequest([file], new ModelChatOptions()), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.Rejected);
        failure.Message.Should().Contain("pictures");
        wire.Requests.Should().BeEmpty();
    }

    #endregion

    #region Reasoning and Tools

    [Fact]
    public async Task Streamed_encrypted_reasoning_goes_back_unchanged_with_the_tool_result_by_call_id()
    {
        var wire = new Wire(
            Stream(
                Event("response.output_item.added", new { output_index = 0, item = new { type = "reasoning", id = "rs_1", summary = Array.Empty<object>() } }),
                Event("response.reasoning_summary_text.delta", new { item_id = "rs_1", output_index = 0, summary_index = 0, delta = "Checking the weather." }),
                Event("response.output_item.done", new { output_index = 0, item = new { type = "reasoning", id = "rs_1", summary = new[] { new { type = "summary_text", text = "Checking the weather." } }, encrypted_content = "gAAAAA-sealed-1" } }),
                Event("response.output_item.done", new { output_index = 1, item = FunctionCall("call_42") }),
                Completed()),
            Reply(Message("Sunny.")));
        // Through the bridge the agent framework uses, so the streamed parts are put together as a run puts them.
        var client = new ModelChatClientBridge(wire.Client());
        var options = new ChatOptions { Tools = [AIFunctionFactory.CreateDeclaration(weather.Name, weather.Description, weather.Schema)] };
        List<ChatMessage> conversation = [new(ChatRole.User, "Weather in Paris?")];

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync(conversation, options, Token))
        {
            updates.Add(update);
        }

        var reply = updates.ToChatResponse();
        reply.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>()
            .Should().Contain(reasoning => reasoning.ProtectedData == "gAAAAA-sealed-1");
        conversation.AddRange(reply.Messages);
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_42", "Sunny")]));
        await client.GetResponseAsync(conversation, options, Token);

        var input = wire.Requests[1].Json.GetProperty("input").EnumerateArray().ToList();
        var sentReasoning = input.Single(item => item.GetProperty("type").GetString() == "reasoning");
        sentReasoning.GetProperty("encrypted_content").GetString().Should().Be("gAAAAA-sealed-1");
        sentReasoning.TryGetProperty("id", out _).Should().BeFalse(
            "with store false the provider keeps no item to look an id up in; the encrypted content carries the reasoning");
        input.Single(item => item.GetProperty("type").GetString() == "function_call").GetProperty("call_id").GetString().Should().Be("call_42");
        var output = input.Single(item => item.GetProperty("type").GetString() == "function_call_output");
        output.GetProperty("call_id").GetString().Should().Be("call_42");
        output.GetProperty("output").GetString().Should().Be("Sunny");
    }

    [Fact]
    public async Task Reasoning_kept_only_as_words_is_not_sent_back()
    {
        var wire = new Wire(Reply(Message("Hello again.")));
        List<ModelChatMessage> conversation =
        [
            new("user", [new ModelText("Hi.")]),
            new("assistant", [new ModelReasoning("The person said hi."), new ModelText("Hello.")]),
            new("assistant", [new ModelReasoning("Only words, from a thread's history.")]),
            new("user", [new ModelText("Hi again.")]),
        ];

        await wire.Client().GetResponse(new ModelChatRequest(conversation, new ModelChatOptions()), Token);

        var input = wire.Requests.Single().Json.GetProperty("input").EnumerateArray().ToList();
        input.Select(item => item.GetProperty("type").GetString()).Should().Equal("message", "message", "message");
    }

    #endregion

    #region Usage and Endings

    [Fact]
    public async Task Usage_carries_cached_and_reasoning_counts_when_sent_and_nulls_when_not()
    {
        var detailed = new { input_tokens = 100, input_tokens_details = new { cached_tokens = 64 }, output_tokens = 30, output_tokens_details = new { reasoning_tokens = 20 }, total_tokens = 130 };
        var bare = new { input_tokens = 100, output_tokens = 30, total_tokens = 130 };
        var wire = new Wire(Reply(Message("a"), usage: detailed), Reply(Message("b"), usage: bare));

        var first = (await wire.Client().GetResponse(Prompt("Say hello."), Token)).Usage!;
        var second = (await wire.Client().GetResponse(Prompt("Say hello."), Token)).Usage!;

        first.Should().BeEquivalentTo(new { InputTokens = 100L, OutputTokens = 30L, TotalTokens = 130L, CachedInputTokens = 64L, ReasoningTokens = 20L });
        second.InputTokens.Should().Be(100);
        second.CachedInputTokens.Should().BeNull("the provider did not say how much was cached");
        second.ReasoningTokens.Should().BeNull("the provider did not say how much was reasoning");
    }

    [Fact]
    public async Task An_answer_cut_short_ends_as_length_whole_and_streamed()
    {
        var wire = new Wire(
            Reply(Message("Partial"), status: "incomplete"),
            Stream([.. TextDeltas("Part", "ial"), Event("response.incomplete", new { response = Response("incomplete") })]));

        var whole = await wire.Client().GetResponse(Prompt("Write a lot."), Token);
        var updates = await Collect(wire.Client().GetStreamingResponse(Prompt("Write a lot."), Token));

        whole.FinishReason.Should().Be("length");
        updates.Select(update => update.FinishReason).OfType<string>().Should().Equal("length");
    }

    [Fact]
    public async Task A_refusal_is_the_reply_and_ends_as_filtered_whole_and_streamed()
    {
        var refusal = new { type = "message", id = "msg_r", role = "assistant", status = "completed", content = new[] { new { type = "refusal", refusal = "I can't help with that." } } };
        var wire = new Wire(
            Reply(refusal),
            Stream(
                Event("response.output_item.added", new { output_index = 0, item = new { type = "message", id = "msg_r", role = "assistant", status = "in_progress", content = Array.Empty<object>() } }),
                Event("response.refusal.delta", new { item_id = "msg_r", output_index = 0, content_index = 0, delta = "I can't help with that." }),
                Event("response.refusal.done", new { item_id = "msg_r", output_index = 0, content_index = 0, refusal = "I can't help with that." }),
                Event("response.output_item.done", new { output_index = 0, item = refusal }),
                Completed()));

        var whole = await wire.Client().GetResponse(Prompt("Something refused."), Token);
        var updates = await Collect(wire.Client().GetStreamingResponse(Prompt("Something refused."), Token));

        whole.FinishReason.Should().Be("content_filter");
        Text(whole.Messages.SelectMany(message => message.Contents)).Should().Be("I can't help with that.");
        updates.Select(update => update.FinishReason).OfType<string>().Should().Equal("content_filter");
        Text(updates.SelectMany(update => update.Contents)).Should().Be("I can't help with that.");
    }

    [Fact]
    public async Task A_stream_that_fails_is_a_failure_of_the_call_not_an_empty_reply()
    {
        var wire = new Wire(Stream(
            [.. TextDeltas("Hel"), Event("response.failed", new { response = Response("failed", error: new { code = "rate_limit_exceeded", message = "Slow down for: the person's text" }) })]));

        var failure = (await FluentActions.Awaiting(() => Collect(wire.Client().GetStreamingResponse(Prompt("Say hello."), Token)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
        failure.Message.Should().NotContain("the person's text", "the provider's own message can quote the input");
    }

    [Fact]
    public async Task An_error_event_in_a_stream_is_a_failure_of_the_call()
    {
        var wire = new Wire(Stream(Event("error", new { code = "server_error", message = "The server had an error.", param = (string?)null })));

        var failure = (await FluentActions.Awaiting(() => Collect(wire.Client().GetStreamingResponse(Prompt("Say hello."), Token)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.Unreachable);
    }

    [Fact]
    public async Task A_whole_answer_that_failed_is_a_failure_of_the_call_not_an_empty_reply()
    {
        var wire = new Wire(new Answer(
            HttpStatusCode.OK,
            "application/json",
            JsonSerializer.Serialize(Response("failed", error: new { code = "server_error", message = "Failed for: the person's text" }))));

        var failure = (await FluentActions.Awaiting(() => wire.Client().GetResponse(Prompt("Say hello."), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.Unreachable);
        failure.Message.Should().NotContain("the person's text");
    }

    [Theory]
    [InlineData("reasoning.summary", "Your organization must be verified to generate reasoning summaries.")]
    [InlineData(null, "Unsupported parameter: 'reasoning.summary' is not supported with this model.")]
    public async Task A_refused_summary_is_dropped_and_the_call_made_once_more_whole_and_streamed(string? param, string message)
    {
        var refusal = new Answer(HttpStatusCode.BadRequest, "application/json", JsonSerializer.Serialize(new { error = new { message, type = "invalid_request_error", param, code = (string?)null } }));
        var wire = new Wire(refusal, Reply(Message("Hi.")), refusal, Stream(TextDeltas("Hi", ".")));

        var whole = await wire.Client().GetResponse(Prompt("Say hello."), Token);
        var streamed = await Collect(wire.Client().GetStreamingResponse(Prompt("Say hello."), Token));

        Text(whole.Messages.SelectMany(item => item.Contents)).Should().Be("Hi.");
        Text(streamed.SelectMany(update => update.Contents)).Should().Be("Hi.");
        wire.Requests.Should().HaveCount(4);
        wire.Requests[0].Json.GetProperty("reasoning").GetProperty("summary").GetString().Should().Be("auto");
        wire.Requests[1].Json.TryGetProperty("reasoning", out _).Should().BeFalse("the second call leaves the summary out");
        wire.Requests[2].Json.GetProperty("reasoning").GetProperty("summary").GetString().Should().Be("auto");
        wire.Requests[3].Json.TryGetProperty("reasoning", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_summary_keeps_the_effort_and_a_second_refusal_is_the_calls_failure()
    {
        var refusal = new Answer(HttpStatusCode.BadRequest, "application/json", JsonSerializer.Serialize(new { error = new { message = "Unsupported parameter: 'reasoning.summary' is not supported with this model.", type = "invalid_request_error", param = "reasoning.summary", code = (string?)null } }));
        var wire = new Wire(refusal, refusal);

        var failure = (await FluentActions.Awaiting(() => wire.Client(new ModelSettings { ReasoningEffort = "low" }).GetResponse(Prompt("Say hello."), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.Rejected);
        wire.Requests.Should().HaveCount(2, "the call is made once more, not again and again");
        var second = wire.Requests[1].Json.GetProperty("reasoning");
        second.GetProperty("effort").GetString().Should().Be("low");
        second.TryGetProperty("summary", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_client_that_was_refused_the_summary_leaves_it_out_for_the_rest_of_its_calls()
    {
        var refusal = new Answer(HttpStatusCode.BadRequest, "application/json", JsonSerializer.Serialize(new { error = new { message = "Your organization must be verified to generate reasoning summaries.", type = "invalid_request_error", param = "reasoning.summary", code = (string?)null } }));
        var wire = new Wire(refusal, Reply(Message("One.")), Reply(Message("Two.")), Stream(TextDeltas("Three.")), Reply(Message("New client.")));
        using var client = wire.Client();

        await client.GetResponse(Prompt("Say one."), Token);
        await client.GetResponse(Prompt("Say two."), Token);
        await Collect(client.GetStreamingResponse(Prompt("Say three."), Token));
        await wire.Client().GetResponse(Prompt("Say hello."), Token);

        wire.Requests.Should().HaveCount(5, "the refusal is paid once, by the first call");
        wire.Requests.Skip(1).Take(3).Select(request => Names(request)).Should().AllSatisfy(names => names.Should().NotContain("reasoning"));
        wire.Requests[4].Json.GetProperty("reasoning").GetProperty("summary").GetString().Should().Be("auto", "a new client asks again");
    }

    [Fact]
    public async Task Another_400_is_not_made_again()
    {
        var refusal = new Answer(HttpStatusCode.BadRequest, "application/json", JsonSerializer.Serialize(new { error = new { message = "Unsupported value: 'minimal'.", type = "invalid_request_error", param = "reasoning.effort", code = (string?)null } }));
        var wire = new Wire(refusal, Reply(Message("unused")));

        await FluentActions.Awaiting(() => wire.Client().GetResponse(Prompt("Say hello."), Token)).Should().ThrowAsync<ModelProviderException>();

        wire.Requests.Should().ContainSingle();
    }

    #endregion

    #region Destination

    [Fact]
    public async Task Requests_go_only_to_the_fixed_address_with_the_key_as_a_bearer()
    {
        var wire = new Wire(Reply(Message("Hi.")), ModelList());
        var elsewhere = new ModelEndpoint(OpenAIProvider.ConnectionKind, new Uri("https://somewhere-else.example.test/v1/"));
        var key = TestKeys.Generate();
        var provider = new OpenAIProvider(new FixtureHttpClientFactory(wire.Handler));

        await provider.CreateChatClient(elsewhere, key, Model, new ModelSettings()).GetResponse(Prompt("Say hello."), Token);
        await provider.ListModels(elsewhere, key, Token);

        wire.Sent.Select(request => request.RequestUri!.AbsoluteUri).Should().Equal("https://api.openai.com/v1/responses", "https://api.openai.com/v1/models");
        wire.Sent.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key);
    }

    [Fact]
    public async Task A_destination_chosen_per_call_sets_the_address_the_credential_and_the_extra_headers()
    {
        var wire = new Wire(Reply(Message("Hi.")));
        var token = TestKeys.Generate();
        var destination = new ResponsesDestination(
            new Uri("https://plan.example.test/backend/"),
            token,
            new Dictionary<string, string> { ["account-id"] = "acct-7" });
        using var client = new ResponsesChatClient(new FixtureHttpClientFactory(wire.Handler).CreateClient("per-call"), destination, Model, new ModelSettings());

        await client.GetResponse(Prompt("Say hello."), Token);

        var sent = wire.Sent.Single();
        sent.RequestUri!.AbsoluteUri.Should().Be("https://plan.example.test/backend/responses");
        sent.Headers.Authorization!.Parameter.Should().Be(token);
        sent.Headers.GetValues("account-id").Should().Equal("acct-7");
    }

    [Fact]
    public async Task The_model_list_names_every_model_and_starts_each_one_permissive()
    {
        var models = await new OpenAIProvider(new FixtureHttpClientFactory(new Wire(ModelList()).Handler))
            .ListModels(connection, TestKeys.Generate(), Token);

        models.Select(model => model.Id).Should().Equal(Model, "text-embedding-test");
        models.Should().OnlyContain(model => model.Capabilities.Vision && model.Capabilities.Reasoning
            && model.Capabilities.StructuredOutput && model.Capabilities.ToolCalling && model.Capabilities.Streaming);
        models.Select(model => model.Capabilities.ReasoningEfforts).Should().AllSatisfy(efforts => efforts.Should().Equal("low", "medium", "high"));
        models.Should().OnlyContain(model => model.Capabilities.ContextTokens == null && model.Pricing.InputPerMillion == null);
    }

    [Fact]
    public async Task Testing_without_a_key_asks_for_one_and_sends_nothing()
    {
        var wire = new Wire(ModelList());

        var result = await new OpenAIProvider(new FixtureHttpClientFactory(wire.Handler)).TestConnection(connection, null, Token);

        result.Should().Be(ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste an OpenAI API key to finish setup."));
        wire.Sent.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static List<string> Names(WireRequest request) => [.. request.Json.EnumerateObject().Select(property => property.Name)];

    private static string Text(IEnumerable<ModelContent> contents) => string.Concat(contents.OfType<ModelText>().Select(content => content.Text));

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates) => await Collect(updates);

    private static async Task<List<ModelChatUpdate>> Collect(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        List<ModelChatUpdate> collected = [];

        await foreach (var update in updates)
        {
            collected.Add(update);
        }

        return collected;
    }

    private static object Response(string status, object[]? output = null, object? usage = null, object? error = null) => new
    {
        id = "resp_wire",
        @object = "response",
        created_at = 1794400000,
        status,
        error,
        incomplete_details = status == "incomplete" ? new { reason = "max_output_tokens" } : null,
        model = Model,
        output = output ?? [],
        usage,
    };

    private static Answer Reply(object item, object? usage = null, string status = "completed") =>
        new(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(Response(status, [item], usage)));

    private static Answer ModelList() => new(
        HttpStatusCode.OK,
        "application/json",
        JsonSerializer.Serialize(new
        {
            @object = "list",
            data = new[]
            {
                new { id = Model, @object = "model", created = 1794400000, owned_by = "system" },
                new { id = "text-embedding-test", @object = "model", created = 1794400000, owned_by = "system" },
            },
        }));

    private static object Message(string text) => new
    {
        type = "message",
        id = "msg_wire",
        role = "assistant",
        status = "completed",
        content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } },
    };

    private static object FunctionCall(string callId) => new
    {
        type = "function_call",
        id = "fc_wire",
        call_id = callId,
        name = "get_weather",
        arguments = """{"city":"Paris"}""",
        status = "completed",
    };

    private static string Event(string type, object fields)
    {
        var json = JsonSerializer.SerializeToNode(fields)!.AsObject();
        json["type"] = type;
        json["sequence_number"] = 0;
        return json.ToJsonString();
    }

    private static string Completed() => Event("response.completed", new { response = Response("completed") });

    private static IEnumerable<string> TextDeltas(params string[] deltas) =>
        deltas.Select(delta => Event("response.output_text.delta", new { item_id = "msg_wire", output_index = 0, content_index = 0, delta }));

    private static Answer Stream(params IEnumerable<string> events) =>
        new(HttpStatusCode.OK, "text/event-stream", string.Concat(events.Select(@event => $"data: {@event}\n\n")));

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

        public IModelChatClient Client(ModelSettings? settings = null) =>
            new OpenAIProvider(new FixtureHttpClientFactory(Handler)).CreateChatClient(connection, TestKeys.Generate(), Model, settings ?? new ModelSettings());

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
