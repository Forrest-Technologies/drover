namespace Drover.Adapters.Tests;

/// <summary>
/// What the Anthropic client puts on the wire and how it reads the answer, against a scripted handler that answers the
/// model's own entry and then each Messages call in turn. These pin what the vendor's chat client does with thinking
/// signatures, cache usage, PDFs and tool results, and what this adapter adds or refuses around it. Shapes from
/// https://platform.claude.com/docs/en/api/messages, https://platform.claude.com/docs/en/build-with-claude/streaming,
/// https://platform.claude.com/docs/en/build-with-claude/prompt-caching and
/// https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview (read 2026-11-27).
/// </summary>
public sealed class AnthropicWireTests
{
    #region Private Fields

    private const string Model = "claude-test-model";

    private static readonly ModelEndpoint connection = new(AnthropicProvider.ConnectionKind, AnthropicProvider.BaseAddress);

    private static readonly ModelTool weather = new(
        "get_weather",
        "Weather for a city",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));

    private static readonly ModelTool time = new(
        "get_time",
        "Local time in a city",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));

    #endregion

    #region Thinking

    [Fact]
    public async Task A_tool_loop_with_thinking_sends_the_streamed_thinking_block_back_unchanged()
    {
        var wire = new Wire(
            Stream("tool_use", Thinking("sig-streamed-1", "I should look up ", "the weather in Paris."), StreamedToolUse("toolu_1", "get_weather")),
            Reply(Message(Text("Sunny."))));
        var bridge = new ModelChatClientBridge(wire.Client());
        var options = new ChatOptions { Tools = [Declared(weather)] };
        List<ChatMessage> conversation = [new(ChatRole.User, "What is the weather in Paris?")];

        List<ChatResponseUpdate> updates = [];

        await foreach (var update in bridge.GetStreamingResponseAsync(conversation, options, Token))
        {
            updates.Add(update);
        }

        var first = updates.ToChatResponse();
        var call = first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
        conversation.AddRange(first.Messages);
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "Sunny, 21 degrees")]));
        await bridge.GetResponseAsync(conversation, options, Token);

        var thinking = AnthropicWire.Blocks(wire.Requests[1], "thinking").Single();
        thinking.GetProperty("thinking").GetString().Should().Be("I should look up the weather in Paris.");
        thinking.GetProperty("signature").GetString().Should().Be("sig-streamed-1");
        AnthropicWire.Blocks(wire.Requests[1], "tool_use").Single().GetProperty("id").GetString().Should().Be("toolu_1");
    }

    [Fact]
    public async Task A_redacted_thinking_block_goes_back_as_redacted_with_its_data()
    {
        var wire = new Wire(
            Reply(Message(new { type = "redacted_thinking", data = "redacted-fixture-1" }, ToolUse("toolu_1", "get_weather")), stop: "tool_use"),
            Reply(Message(Text("Sunny."))));
        var bridge = new ModelChatClientBridge(wire.Client());
        var options = new ChatOptions { Tools = [Declared(weather)] };
        List<ChatMessage> conversation = [new(ChatRole.User, "What is the weather in Paris?")];

        var first = await bridge.GetResponseAsync(conversation, options, Token);
        var call = first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
        conversation.AddRange(first.Messages);
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "Sunny")]));
        await bridge.GetResponseAsync(conversation, options, Token);

        AnthropicWire.Blocks(wire.Requests[1], "redacted_thinking").Single().GetProperty("data").GetString().Should().Be("redacted-fixture-1");
        AnthropicWire.Blocks(wire.Requests[1], "thinking").Should().BeEmpty("withheld reasoning is not sent back as words");
    }

    [Fact]
    public async Task Reasoning_kept_only_as_words_is_not_sent_back()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));
        List<ModelChatMessage> conversation =
        [
            new("user", [new ModelText("Hi")]),
            new("assistant", [new ModelReasoning("Thinking about a greeting."), new ModelText("Hello.")]),
            new("user", [new ModelText("And again?")]),
        ];

        await wire.Client().GetResponse(new ModelChatRequest(conversation, new ModelChatOptions()), Token);

        AnthropicWire.Blocks(wire.Requests.Single(), "thinking", "redacted_thinking").Should().BeEmpty("the provider refuses a thinking block with no signature");
    }

    [Fact]
    public async Task A_thinking_model_is_asked_for_adaptive_thinking_with_summaries_and_the_effort_only_when_set()
    {
        var wire = new Wire(Reply(Message(Text("a"))), Reply(Message(Text("b"))));

        await wire.Client().GetResponse(Prompt("Say hello."), Token);
        await wire.Client(new ModelSettings { ReasoningEffort = "xhigh" }).GetResponse(Prompt("Say hello."), Token);

        var thinking = wire.Requests.Select(request => request.Json.GetProperty("thinking")).ToList();
        thinking.Should().OnlyContain(value => value.GetProperty("type").GetString() == "adaptive"
            && value.GetProperty("display").GetString() == "summarized");
        wire.Requests[0].Json.TryGetProperty("output_config", out _).Should().BeFalse("a bot with no effort leaves it to the model");
        wire.Requests[1].Json.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("xhigh");
    }

    [Fact]
    public async Task A_model_that_does_not_take_adaptive_thinking_is_sent_no_thinking()
    {
        var wire = new Wire(Reply(Message(Text("a")))) { Entry = ModelEntry(thinks: false) };

        await wire.Client().GetResponse(Prompt("Say hello."), Token);

        wire.Requests.Single().Json.TryGetProperty("thinking", out _).Should().BeFalse();
    }

    #endregion

    #region Tools

    [Fact]
    public async Task Two_tool_results_of_one_turn_go_back_in_one_user_message()
    {
        var wire = new Wire(
            Reply(Message(ToolUse("toolu_1", "get_weather"), ToolUse("toolu_2", "get_time")), stop: "tool_use"),
            Reply(Message(Text("Sunny, and it is noon."))));
        var bridge = new ModelChatClientBridge(wire.Client());
        var options = new ChatOptions { Tools = [Declared(weather), Declared(time)] };
        List<ChatMessage> conversation = [new(ChatRole.User, "Weather and time in Paris?")];

        var first = await bridge.GetResponseAsync(conversation, options, Token);
        conversation.AddRange(first.Messages);
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_1", "Sunny")]));
        conversation.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("toolu_2", "Noon")]));
        await bridge.GetResponseAsync(conversation, options, Token);

        var messages = wire.Requests[1].Json.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal("user", "assistant", "user");
        messages[^1].GetProperty("content").EnumerateArray().Select(block => block.GetProperty("tool_use_id").GetString())
            .Should().Equal("toolu_1", "toolu_2");
    }

    [Fact]
    public async Task A_picture_after_the_tool_results_follows_them_in_the_same_user_message()
    {
        var wire = new Wire(Reply(Message(Text("I see a screen."))));
        List<ModelChatMessage> conversation =
        [
            new("user", [new ModelText("Take a screenshot.")]),
            new("assistant", [new ModelToolCall("toolu_1", "screenshot", JsonSerializer.SerializeToElement(new { }))]),
            new("tool", [new ModelToolResult("toolu_1", JsonSerializer.SerializeToElement("Taken."))]),
            new("user", [new ModelText("The screenshot:"), new ModelData(new byte[] { 137, 80, 78, 71 }, "image/png")]),
        ];

        await wire.Client().GetResponse(new ModelChatRequest(conversation, new ModelChatOptions()), Token);

        var last = wire.Requests.Single().Json.GetProperty("messages").EnumerateArray().Last();
        last.GetProperty("role").GetString().Should().Be("user");
        last.GetProperty("content").EnumerateArray().Select(block => block.GetProperty("type").GetString())
            .Should().Equal("tool_result", "text", "image");
    }

    [Fact]
    public async Task A_forced_tool_choice_is_refused_in_words_and_nothing_is_sent()
    {
        var wire = new Wire(Reply(Message(Text("never"))));
        var any = new ModelChatOptions { Tools = [weather], ToolMode = "required" };
        var named = new ModelChatOptions { Tools = [weather], ToolMode = "required", RequiredToolName = "get_weather" };

        foreach (var options in new[] { any, named })
        {
            var failure = (await FluentActions.Awaiting(() => wire.Client().GetResponse(Prompt("Weather?") with { Options = options }, Token))
                .Should().ThrowAsync<ModelProviderException>()).Which;
            failure.Failure.Should().Be(ModelFailure.Rejected);
            failure.Message.Should().Contain("tool");
        }

        wire.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_tool_choice_is_automatic_and_sent_only_with_tools()
    {
        var wire = new Wire(Reply(Message(Text("a"))), Reply(Message(Text("b"))));

        await wire.Client().GetResponse(Prompt("Say hello."), Token);
        await wire.Client().GetResponse(Prompt("Weather?") with { Options = new ModelChatOptions { Tools = [weather], AllowMultipleToolCalls = false } }, Token);

        wire.Requests[0].Json.TryGetProperty("tool_choice", out _).Should().BeFalse();
        wire.Requests[0].Json.TryGetProperty("tools", out _).Should().BeFalse();
        var choice = wire.Requests[1].Json.GetProperty("tool_choice");
        choice.GetProperty("type").GetString().Should().Be("auto");
        choice.GetProperty("disable_parallel_tool_use").GetBoolean().Should().BeTrue();
        wire.Requests[1].Json.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("properties").TryGetProperty("city", out _).Should().BeTrue();
    }

    #endregion

    #region Caching

    [Fact]
    public async Task One_breakpoint_follows_the_instructions_and_the_automatic_one_ends_the_conversation()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));
        var options = new ModelChatOptions { Instructions = "Be brief.", Tools = [weather, time] };

        await wire.Client().GetResponse(Prompt("Say hello.") with { Options = options }, Token);

        var sent = wire.Requests.Single().Json;
        sent.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        var system = sent.GetProperty("system").EnumerateArray().ToList();
        system.Should().ContainSingle().Which.GetProperty("text").GetString().Should().Be("Be brief.");
        system[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        sent.GetProperty("tools").EnumerateArray().Select(tool => tool.TryGetProperty("cache_control", out var _)).Should().AllBeEquivalentTo(
            false,
            "the system block's breakpoint already covers the tools before it");
        CacheBreakpoints(sent).Should().Be(2, "one after the instructions and the automatic one");
    }

    [Fact]
    public async Task With_no_instructions_the_breakpoint_follows_the_last_tool()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));

        await wire.Client().GetResponse(Prompt("Weather?") with { Options = new ModelChatOptions { Tools = [weather, time] } }, Token);

        var tools = wire.Requests.Single().Json.GetProperty("tools").EnumerateArray().ToList();
        tools[0].TryGetProperty("cache_control", out _).Should().BeFalse();
        tools[1].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        wire.Requests.Single().Json.TryGetProperty("system", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_second_identical_request_reports_cache_reads_within_its_input_and_the_writes_by_name()
    {
        var wire = new Wire(
            Reply(Message(Text("first")), usage: new { input_tokens = 20, output_tokens = 5, cache_creation_input_tokens = 1800, cache_read_input_tokens = 0 }),
            Reply(Message(Text("second")), usage: new { input_tokens = 20, output_tokens = 5, cache_creation_input_tokens = 0, cache_read_input_tokens = 1800 }));
        var client = wire.Client();

        var first = await client.GetResponse(Prompt("Say hello."), Token);
        var second = await client.GetResponse(Prompt("Say hello."), Token);

        first.Usage!.InputTokens.Should().Be(1820);
        first.Usage.AdditionalCounts.Should().ContainKey(AnthropicChatClient.CacheWriteTokens).WhoseValue.Should().Be(1800);
        second.Usage!.CachedInputTokens.Should().Be(1800);
        second.Usage.InputTokens.Should().Be(1820, "input tokens include the ones read from the cache");
        wire.Requests.Should().HaveCount(2);
        wire.ModelReads.Should().Be(1, "one client reads the model's entry once");
    }

    #endregion

    #region Request Settings

    [Fact]
    public async Task With_no_output_limit_the_request_asks_for_the_models_own_longest_answer()
    {
        var wire = new Wire(Reply(Message(Text("a"))), Reply(Message(Text("b"))), Reply(Message(Text("c"))));

        await wire.Client().GetResponse(Prompt("Say hello."), Token);
        await wire.Client(new ModelSettings { MaxOutputTokens = 2000 }).GetResponse(Prompt("Say hello."), Token);
        await wire.Client(new ModelSettings { MaxOutputTokens = 900_000 }).GetResponse(Prompt("Say hello."), Token);

        wire.Requests.Select(request => request.Json.GetProperty("max_tokens").GetInt32()).Should().Equal(128_000, 2000, 128_000);
    }

    [Fact]
    public async Task Sampling_settings_are_never_sent()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));
        var options = new ModelChatOptions { Temperature = 0.2f, TopP = 0.9f, TopK = 40, FrequencyPenalty = 0.5f, PresencePenalty = 0.5f, Seed = 7 };

        await wire.Client(new ModelSettings { Temperature = 0.7 }).GetResponse(Prompt("Say hello.") with { Options = options }, Token);

        var names = wire.Requests.Single().Json.EnumerateObject().Select(property => property.Name).ToList();
        names.Should().NotContain(["temperature", "top_p", "top_k", "frequency_penalty", "presence_penalty", "seed"]);
    }

    [Fact]
    public async Task A_schema_becomes_the_output_format_and_a_bare_json_request_is_refused_in_words()
    {
        var wire = new Wire(Reply(Message(Text("{\"city\":\"Paris\"}"))));
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } });

        await wire.Client().GetResponse(Prompt("Weather?") with { Options = new ModelChatOptions { ResponseFormat = "json", ResponseSchema = schema } }, Token);
        var bare = await FluentActions.Awaiting(() => wire.Client().GetResponse(Prompt("Weather?") with { Options = new ModelChatOptions { ResponseFormat = "json" } }, Token))
            .Should().ThrowAsync<ModelProviderException>();

        var format = wire.Requests.Single().Json.GetProperty("output_config").GetProperty("format");
        format.GetProperty("type").GetString().Should().Be("json_schema");
        format.GetProperty("schema").GetProperty("properties").TryGetProperty("city", out _).Should().BeTrue();
        bare.Which.Message.Should().Contain("schema");
    }

    [Fact]
    public async Task A_pdf_goes_as_a_document_a_picture_as_an_image_and_any_other_file_is_refused()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));
        byte[] pdf = [37, 80, 68, 70];
        byte[] png = [137, 80, 78, 71];
        var request = new ModelChatRequest(
            [new ModelChatMessage("user", [new ModelText("Read these."), new ModelData(pdf, "application/pdf"), new ModelData(png, "image/png")])],
            new ModelChatOptions());

        await wire.Client().GetResponse(request, Token);
        var other = new ModelChatRequest([new ModelChatMessage("user", [new ModelData(png, "application/zip")])], new ModelChatOptions());
        var refused = await FluentActions.Awaiting(() => wire.Client().GetResponse(other, Token)).Should().ThrowAsync<ModelProviderException>();

        var content = wire.Requests.Single().Json.GetProperty("messages")[0].GetProperty("content");
        content[1].GetProperty("type").GetString().Should().Be("document");
        content[1].GetProperty("source").GetProperty("data").GetString().Should().Be(Convert.ToBase64String(pdf));
        content[2].GetProperty("type").GetString().Should().Be("image");
        content[2].GetProperty("source").GetProperty("media_type").GetString().Should().Be("image/png");
        refused.Which.Failure.Should().Be(ModelFailure.Rejected);
    }

    [Fact]
    public async Task Every_request_goes_to_the_fixed_address_with_the_key_as_a_bearer_and_the_api_version()
    {
        var wire = new Wire(Reply(Message(Text("ok"))));
        var key = TestKeys.Generate();
        var elsewhere = new ModelEndpoint(AnthropicProvider.ConnectionKind, new Uri("https://models.example.test/"));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(wire.Handler));

        await provider.CreateChatClient(elsewhere, key, Model, new ModelSettings()).GetResponse(Prompt("Say hello."), Token);
        await provider.ListModels(elsewhere, key, Token);
        await provider.TestConnection(elsewhere, key, Token);

        wire.Sent.Should().HaveCount(4);
        wire.Sent.Should().OnlyContain(request => request.RequestUri!.Host == "api.anthropic.com");
        wire.Sent.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key);
        wire.Sent.Should().OnlyContain(request => request.Headers.GetValues("anthropic-version").Single() == "2026-10-21");
        wire.Sent.Should().OnlyContain(request => !request.Headers.Contains("x-api-key"));
        wire.Sent[0].RequestUri!.AbsolutePath.Should().Be($"/v1/models/{Model}");
        wire.Sent[1].RequestUri!.AbsolutePath.Should().Be("/v1/messages");
    }

    #endregion

    #region Replies

    [Fact]
    public async Task Stop_reasons_become_drovers_finish_reasons()
    {
        var wire = new Wire(
            Reply(Message(Text("a")), stop: "end_turn"),
            Reply(Message(Text("b")), stop: "max_tokens"),
            Reply(Message(ToolUse("toolu_1", "get_weather")), stop: "tool_use"),
            Reply(Message(Text("c")), stop: "refusal"));
        var client = wire.Client();

        List<string?> reasons = [];

        for (var index = 0; index < 4; index++)
        {
            reasons.Add((await client.GetResponse(Prompt("Say hello."), Token)).FinishReason);
        }

        reasons.Should().Equal("stop", "length", "tool_calls", "content_filter");
    }

    [Fact]
    public async Task A_streamed_reply_carries_its_thinking_text_and_signature_and_usage()
    {
        var wire = new Wire(Stream("end_turn", Thinking("sig-2", "Ten percent ", "is 24."), TextBlock("It is ", "36.")));

        var updates = await Collect(wire.Client().GetStreamingResponse(Prompt("What is 15 percent of 240?"), Token));
        var contents = updates.SelectMany(update => update.Contents).ToList();

        string.Concat(contents.OfType<ModelReasoning>().Select(part => part.Text)).Should().Be("Ten percent is 24.");
        contents.OfType<ModelReasoning>().Select(part => part.ProtectedData).OfType<string>().Should().Equal("sig-2");
        string.Concat(contents.OfType<ModelText>().Select(part => part.Text)).Should().Be("It is 36.");
        contents.OfType<ModelTokenUsage>().Should().NotBeEmpty();
        updates.Last().FinishReason.Should().Be("stop");
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static AIFunctionDeclaration Declared(ModelTool tool) =>
        AIFunctionFactory.CreateDeclaration(tool.Name, tool.Description, tool.Schema);

    private static int CacheBreakpoints(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Sum(property => property.Name == "cache_control" ? 1 : CacheBreakpoints(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Sum(CacheBreakpoints),
        _ => 0,
    };

    private static async Task<List<ModelChatUpdate>> Collect(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        List<ModelChatUpdate> collected = [];

        await foreach (var update in updates)
        {
            collected.Add(update);
        }

        return collected;
    }

    private static object ModelEntry(bool thinks = true) => new
    {
        type = "model",
        id = Model,
        display_name = "Claude Test Model",
        max_input_tokens = 1_000_000,
        max_tokens = 128_000,
        capabilities = new
        {
            image_input = new { supported = true },
            pdf_input = new { supported = true },
            structured_outputs = new { supported = true },
            effort = new { supported = true, low = new { supported = true }, medium = new { supported = true }, high = new { supported = true } },
            thinking = new { supported = thinks, types = new { adaptive = new { supported = thinks }, enabled = new { supported = false } } },
        },
    };

    private static object Text(string text) => new { type = "text", text };

    private static object ToolUse(string id, string name) => new { type = "tool_use", id, name, input = new { city = "Paris" } };

    private static object[] Message(params object[] content) => content;

    private static Answer Reply(object content, string stop = "end_turn", object? usage = null) => new(
        HttpStatusCode.OK,
        "application/json",
        JsonSerializer.Serialize(new
        {
            id = "msg_wire_1",
            type = "message",
            role = "assistant",
            model = Model,
            content,
            stop_reason = stop,
            stop_sequence = (string?)null,
            usage = usage ?? new { input_tokens = 12, output_tokens = 6, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
        }));

    private static IEnumerable<object> Thinking(string signature, params string[] parts) =>
    [
        new { type = "content_block_start", content_block = new { type = "thinking", thinking = string.Empty, signature = string.Empty } },
        .. parts.Select(part => (object)new { type = "content_block_delta", delta = new { type = "thinking_delta", thinking = part } }),
        new { type = "content_block_delta", delta = new { type = "signature_delta", signature } },
        new { type = "content_block_stop" },
    ];

    private static IEnumerable<object> TextBlock(params string[] parts) =>
    [
        new { type = "content_block_start", content_block = new { type = "text", text = string.Empty } },
        .. parts.Select(part => (object)new { type = "content_block_delta", delta = new { type = "text_delta", text = part } }),
        new { type = "content_block_stop" },
    ];

    private static IEnumerable<object> StreamedToolUse(string id, string name) =>
    [
        new { type = "content_block_start", content_block = new { type = "tool_use", id, name, input = new { } } },
        new { type = "content_block_delta", delta = new { type = "input_json_delta", partial_json = "{\"city\":\"Paris\"}" } },
        new { type = "content_block_stop" },
    ];

    /// <summary>A stream of blocks, each numbered by its place, between the message's start and its end.</summary>
    private static Answer Stream(string stop, params IEnumerable<object>[] blocks)
    {
        var body = new StringBuilder();
        Add("message_start", new
        {
            type = "message_start",
            message = new { id = "msg_stream", type = "message", role = "assistant", model = Model, content = Array.Empty<object>(), usage = new { input_tokens = 12, output_tokens = 1 } },
        });
        for (var index = 0; index < blocks.Length; index++)
        {
            foreach (var @event in blocks[index])
            {
                var element = JsonSerializer.SerializeToNode(@event)!.AsObject();
                element["index"] = index;
                Add(element["type"]!.GetValue<string>(), element);
            }
        }

        Add("message_delta", new { type = "message_delta", delta = new { stop_reason = stop }, usage = new { output_tokens = 9 } });
        Add("message_stop", new { type = "message_stop" });
        return new(HttpStatusCode.OK, "text/event-stream", body.ToString());

        void Add(string name, object data) => body.Append($"event: {name}\ndata: {JsonSerializer.Serialize(data)}\n\n");
    }

    private sealed record Answer(HttpStatusCode Status, string ContentType, string Body);

    /// <summary>Answers the model's entry from <see cref="Entry"/> and each Messages call with the next answer.</summary>
    private sealed class Wire
    {
        #region Private Fields

        private readonly Answer[] answers;

        private int answered;

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

        public object Entry { get; init; } = ModelEntry();

        public List<HttpRequestMessage> Sent { get; } = [];

        /// <summary>The Messages calls, leaving out the model reads.</summary>
        public List<WireRequest> Requests { get; } = [];

        public int ModelReads { get; private set; }

        #endregion

        #region Public Methods

        public IModelChatClient Client(ModelSettings? settings = null) =>
            new AnthropicProvider(new FixtureHttpClientFactory(Handler)).CreateChatClient(connection, TestKeys.Generate(), Model, settings ?? new ModelSettings());

        #endregion

        #region Private Methods

        private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, int count, CancellationToken cancellation)
        {
            Sent.Add(request);

            if (request.Method == HttpMethod.Get)
            {
                ModelReads++;
                var listed = request.RequestUri!.AbsolutePath.EndsWith("/v1/models", StringComparison.Ordinal);
                return ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(listed ? new { data = new[] { Entry }, has_more = false } : Entry));
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
            Requests.Add(new WireRequest(request.Method.Method, request.RequestUri!, body));
            var answer = answers[answered++];
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
