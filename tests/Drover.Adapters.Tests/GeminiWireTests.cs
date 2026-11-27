namespace Drover.Adapters.Tests;

/// <summary>
/// What the Gemini client puts on the wire and how it reads the answer, against a scripted handler. The thought signature
/// rules (a signature goes back on the part it came on, parallel calls are followed by all their responses in the same
/// order) are from https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/thinking/thought-signatures; the
/// request and answer shapes are from https://ai.google.dev/api/generate-content and https://ai.google.dev/api/models
/// (read 2026-11-27).
/// </summary>
public sealed class GeminiWireTests
{
    #region Private Fields

    private const string Signature = "c2lnbmF0dXJlLUE=";

    private static readonly ModelTool weather = new(
        "get_weather",
        "Weather for a city",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } }));

    #endregion

    #region Request Shape

    [Fact]
    public async Task The_key_goes_in_a_header_and_in_no_address()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")]), ScriptedAnswer.Stream(ScriptedAnswer.Chunk([Text("b")])), ModelListAnswer());
        var client = script.Client();

        await client.GetResponse(Prompt("Hi."), Token);
        await Drain(client.GetStreamingResponse(Prompt("Hi."), Token));
        await script.Provider.ListModels(GeminiScript.Connection, script.Key, Token);

        script.Sent.Should().HaveCount(3);
        script.Sent.Should().OnlyContain(request => request.Headers["x-goog-api-key"] == script.Key);
        script.Sent.Should().OnlyContain(request => !request.Uri.ToString().Contains(script.Key) && !request.Body.Contains(script.Key));
        script.Sent[0].Uri.AbsolutePath.Should().Be($"/v1beta/models/{GeminiScript.Model}:generateContent");
        script.Sent[1].Uri.PathAndQuery.Should().Be($"/v1beta/models/{GeminiScript.Model}:streamGenerateContent?alt=sse");
        script.Sent.Should().OnlyContain(request => request.Uri.Host == "generativelanguage.googleapis.com");
    }

    [Fact]
    public async Task The_bots_instructions_are_the_system_instruction_and_nothing_is_stored_at_the_provider()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")]));
        var request = Prompt("Hi.") with { Options = new ModelChatOptions { Instructions = "Be brief." } };

        await script.Client().GetResponse(request, Token);

        var body = script.Sent.Single().Json;
        body.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("Be brief.");
        body.GetProperty("store").GetBoolean().Should().BeFalse();
        body.GetProperty("contents")[0].GetProperty("role").GetString().Should().Be("user");
        body.GetProperty("contents").GetArrayLength().Should().Be(1, "the instructions are not a turn");
    }

    [Fact]
    public async Task A_schema_request_sets_the_json_mime_type_and_the_json_schema()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("{}")]));
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } } });

        await script.Client().GetResponse(Prompt("Report.") with { Options = new ModelChatOptions { ResponseFormat = "json", ResponseSchema = schema } }, Token);

        var config = script.Sent.Single().Json.GetProperty("generationConfig");
        config.GetProperty("responseMimeType").GetString().Should().Be("application/json");
        config.GetProperty("responseJsonSchema").GetProperty("properties").GetProperty("city").GetProperty("type").GetString().Should().Be("string");
        config.TryGetProperty("responseSchema", out _).Should().BeFalse("the provider marks responseSchema as deprecated");
    }

    [Fact]
    public async Task Asks_for_thought_summaries_and_sends_a_level_only_when_an_effort_is_set()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")]));
        var client = script.Client();

        await client.GetResponse(Prompt("Hi."), Token);
        await client.GetResponse(Prompt("Hi.") with { Options = new ModelChatOptions { ReasoningEffort = "high" } }, Token);
        await script.Client(new ModelSettings { ReasoningEffort = "low" }).GetResponse(Prompt("Hi."), Token);

        var thinking = script.Sent.Select(request => request.Json.GetProperty("generationConfig").GetProperty("thinkingConfig")).ToList();
        thinking.Should().OnlyContain(value => value.GetProperty("includeThoughts").GetBoolean());
        thinking[0].TryGetProperty("thinkingLevel", out _).Should().BeFalse("a bot with no effort leaves it to the model");
        thinking[1].GetProperty("thinkingLevel").GetString().Should().Be("HIGH");
        thinking[2].GetProperty("thinkingLevel").GetString().Should().Be("LOW", "the bot's own setting is the default");
    }

    [Fact]
    public async Task A_model_that_does_not_think_is_asked_again_once_without_thoughts_and_then_left_alone()
    {
        var refusal = ScriptedAnswer.Json(
            new { error = new { code = 400, message = "Thinking is not supported by this model.", status = "INVALID_ARGUMENT" } },
            HttpStatusCode.BadRequest);
        var script = new GeminiScript(refusal, ScriptedAnswer.Reply([Text("a")]), ScriptedAnswer.Reply([Text("b")]));
        var client = script.Client();

        await client.GetResponse(Prompt("Hi."), Token);
        await client.GetResponse(Prompt("Hi."), Token);

        script.Sent.Should().HaveCount(3);
        script.Sent[0].Json.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _).Should().BeTrue();
        script.Sent[1].Body.Should().NotContain("thinkingConfig");
        script.Sent[2].Body.Should().NotContain("thinkingConfig", "the refusal is remembered for the rest of the client's calls");
    }

    [Fact]
    public async Task A_refused_effort_is_the_providers_own_failure_and_is_not_asked_again()
    {
        var refusal = ScriptedAnswer.Json(
            new { error = new { code = 400, message = "thinking_level is not supported.", status = "INVALID_ARGUMENT" } },
            HttpStatusCode.BadRequest);
        var script = new GeminiScript(refusal);

        var failure = await FluentActions
            .Awaiting(() => script.Client(new ModelSettings { ReasoningEffort = "high" }).GetResponse(Prompt("Hi."), Token))
            .Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
        script.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Pictures_go_as_inline_data_and_other_files_are_refused()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")]));
        var picture = new ModelChatMessage("user", [new ModelText("What is this?"), new ModelData(new byte[] { 1, 2, 3 }, "image/png")]);

        await script.Client().GetResponse(new ModelChatRequest([picture], new ModelChatOptions()), Token);
        var pdf = new ModelChatMessage("user", [new ModelData(new byte[] { 1 }, "application/pdf")]);
        var refused = await FluentActions
            .Awaiting(() => script.Client().GetResponse(new ModelChatRequest([pdf], new ModelChatOptions()), Token))
            .Should().ThrowAsync<ModelProviderException>();

        var part = script.Sent.Single().Json.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData");
        part.GetProperty("mimeType").GetString().Should().Be("image/png");
        part.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        refused.Which.Failure.Should().Be(ModelFailure.Rejected);
    }

    #endregion

    #region Thought Signatures

    [Fact]
    public async Task A_signature_on_a_call_is_carried_as_protected_reasoning_before_the_call_and_thought_text_stays_text()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Thought("Checking."), Call("call_1", "Paris", Signature)]));

        var response = await script.Client().GetResponse(Prompt("Weather?"), Token);

        response.Messages.Single().Contents.Should().SatisfyRespectively(
            content => content.Should().Be(new ModelReasoning("Checking.")),
            content => content.Should().Be(new ModelReasoning(string.Empty, ProtectedData: Signature)),
            content => content.Should().BeOfType<ModelToolCall>().Which.CallId.Should().Be("call_1"));
    }

    [Fact]
    public async Task One_call_round_trips_with_its_signature_unchanged()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Call("call_1", "Paris", Signature)]), ScriptedAnswer.Reply([Text("Sunny.")]));
        var client = script.Client();
        var options = new ModelChatOptions { Tools = [weather] };
        List<ModelChatMessage> conversation = [new("user", [new ModelText("Weather?")])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), Token);
        conversation.AddRange(first.Messages);
        conversation.Add(new("tool", [new ModelToolResult("call_1", JsonSerializer.SerializeToElement("Sunny"))]));
        await client.GetResponse(new ModelChatRequest(conversation, options), Token);

        var contents = script.Sent[1].Json.GetProperty("contents");
        contents.GetArrayLength().Should().Be(3);
        var call = contents[1];
        call.GetProperty("role").GetString().Should().Be("model");
        call.GetProperty("parts").GetArrayLength().Should().Be(1);
        call.GetProperty("parts")[0].GetProperty("thoughtSignature").GetString().Should().Be(Signature);
        call.GetProperty("parts")[0].GetProperty("functionCall").GetProperty("name").GetString().Should().Be("get_weather");
        var answer = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        contents[2].GetProperty("role").GetString().Should().Be("user");
        answer.GetProperty("name").GetString().Should().Be("get_weather");
        answer.GetProperty("id").GetString().Should().Be("call_1");
        answer.GetProperty("response").GetProperty("result").GetString().Should().Be("Sunny");
    }

    [Fact]
    public async Task Two_parallel_calls_go_back_as_both_calls_and_then_both_responses_in_order_with_only_the_first_signed()
    {
        var script = new GeminiScript(
            ScriptedAnswer.Reply([Call("call_1", "Paris", Signature), Call("call_2", "London", null)]),
            ScriptedAnswer.Reply([Text("Both sunny.")]));
        var client = script.Client();
        var options = new ModelChatOptions { Tools = [weather] };
        List<ModelChatMessage> conversation = [new("user", [new ModelText("Paris and London?")])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), Token);
        conversation.AddRange(first.Messages);

        // The results come back as separate messages, the second call's first.
        conversation.Add(new("tool", [new ModelToolResult("call_2", JsonSerializer.SerializeToElement("Rain"))]));
        conversation.Add(new("tool", [new ModelToolResult("call_1", JsonSerializer.SerializeToElement("Sun"))]));
        await client.GetResponse(new ModelChatRequest(conversation, options), Token);

        var contents = script.Sent[1].Json.GetProperty("contents");
        contents.GetArrayLength().Should().Be(3, "all calls are one turn and all responses are one turn");
        var calls = contents[1].GetProperty("parts");
        calls.GetArrayLength().Should().Be(2);
        calls[0].GetProperty("thoughtSignature").GetString().Should().Be(Signature);
        calls[1].TryGetProperty("thoughtSignature", out _).Should().BeFalse("only the first call carries a signature");
        calls[0].GetProperty("functionCall").GetProperty("id").GetString().Should().Be("call_1");
        var responses = contents[2].GetProperty("parts");
        responses.EnumerateArray().Select(part => part.GetProperty("functionResponse").GetProperty("id").GetString())
            .Should().Equal("call_1", "call_2");
        responses.EnumerateArray().Select(part => part.GetProperty("functionResponse").GetProperty("response").GetProperty("result").GetString())
            .Should().Equal("Sun", "Rain");
    }

    [Fact]
    public async Task A_call_the_provider_gave_no_id_gets_one_that_is_not_sent_back_and_responses_follow_the_order_of_the_calls()
    {
        var script = new GeminiScript(
            ScriptedAnswer.Reply([Call(null, "Paris", Signature), Call(null, "London", null)]),
            ScriptedAnswer.Reply([Text("Done.")]));
        var client = script.Client();
        var options = new ModelChatOptions { Tools = [weather] };
        List<ModelChatMessage> conversation = [new("user", [new ModelText("Paris and London?")])];

        var first = await client.GetResponse(new ModelChatRequest(conversation, options), Token);
        var calls = first.Messages.Single().Contents.OfType<ModelToolCall>().ToList();
        calls.Select(call => call.CallId).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.Length > 0);
        conversation.AddRange(first.Messages);
        conversation.Add(new("tool", [
            new ModelToolResult(calls[1].CallId, JsonSerializer.SerializeToElement("Rain")),
            new ModelToolResult(calls[0].CallId, JsonSerializer.SerializeToElement("Sun")),
        ]));
        await client.GetResponse(new ModelChatRequest(conversation, options), Token);

        var contents = script.Sent[1].Json.GetProperty("contents");
        contents[1].GetProperty("parts").EnumerateArray().Should().OnlyContain(part => !HasId(part.GetProperty("functionCall")));
        var responses = contents[2].GetProperty("parts").EnumerateArray().Select(part => part.GetProperty("functionResponse")).ToList();
        responses.Should().OnlyContain(response => !HasId(response));
        responses.Select(response => response.GetProperty("response").GetProperty("result").GetString()).Should().Equal("Sun", "Rain");
    }

    [Fact]
    public async Task Reasoning_without_protected_data_is_not_sent_back()
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")]));
        List<ModelChatMessage> conversation =
        [
            new("user", [new ModelText("Hi.")]),
            new("assistant", [new ModelReasoning("I thought about it."), new ModelText("Hello."), new ModelReasoning(string.Empty, ProtectedData: Signature)]),
            new("user", [new ModelText("Again.")]),
        ];

        await script.Client().GetResponse(new ModelChatRequest(conversation, new ModelChatOptions()), Token);

        script.Sent.Single().Body.Should().NotContain("I thought about it.");
        script.Sent.Single().Json.GetProperty("contents")[1].GetProperty("parts").GetArrayLength().Should().Be(1);
    }

    #endregion

    #region Answers

    [Fact]
    public async Task Usage_counts_thoughts_as_output_and_reports_cached_and_thought_tokens()
    {
        var usage = new { promptTokenCount = 100, candidatesTokenCount = 10, thoughtsTokenCount = 30, cachedContentTokenCount = 64, totalTokenCount = 140 };
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("a")], usage: usage));

        var response = await script.Client().GetResponse(Prompt("Hi."), Token);

        response.Usage.Should().BeEquivalentTo(new ModelTokenUsage(100, 40, 140) { CachedInputTokens = 64, ReasoningTokens = 30 });
    }

    [Theory]
    [InlineData("STOP", "stop")]
    [InlineData("MAX_TOKENS", "length")]
    [InlineData("SAFETY", "content_filter")]
    [InlineData("RECITATION", "content_filter")]
    [InlineData("PROHIBITED_CONTENT", "content_filter")]
    [InlineData("BLOCKLIST", "content_filter")]
    public async Task A_stop_is_a_finish_reason_and_a_safety_stop_is_content_filter_not_an_exception(string provider, string expected)
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([Text("partial")], provider));

        var response = await script.Client().GetResponse(Prompt("Hi."), Token);

        response.FinishReason.Should().Be(expected);
    }

    [Theory]
    [InlineData("MALFORMED_FUNCTION_CALL")]
    [InlineData("MISSING_THOUGHT_SIGNATURE")]
    public async Task A_malformed_call_or_a_missing_signature_is_a_rejection_that_names_the_reason(string reason)
    {
        var script = new GeminiScript(ScriptedAnswer.Reply([], reason));

        var failure = await FluentActions.Awaiting(() => script.Client().GetResponse(Prompt("Hi."), Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
        failure.Which.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task A_prompt_the_provider_blocked_ends_as_content_filter()
    {
        var script = new GeminiScript(ScriptedAnswer.Json(new { promptFeedback = new { blockReason = "SAFETY" } }));

        var response = await script.Client().GetResponse(Prompt("Hi."), Token);

        response.FinishReason.Should().Be("content_filter");
    }

    [Fact]
    public async Task A_stream_reports_usage_once_at_the_end_and_a_signature_that_arrives_in_an_empty_part()
    {
        var usage = new { promptTokenCount = 5, candidatesTokenCount = 2, thoughtsTokenCount = 3, totalTokenCount = 10 };
        var script = new GeminiScript(ScriptedAnswer.Stream(
            ScriptedAnswer.Chunk([Text("Hi")], finish: null, usage: usage),
            ScriptedAnswer.Chunk([new { text = string.Empty, thoughtSignature = Signature }], finish: "STOP", usage: usage)));

        List<ModelChatUpdate> updates = [];
        await foreach (var update in script.Client().GetStreamingResponse(Prompt("Hi."), Token))
        {
            updates.Add(update);
        }

        var contents = updates.SelectMany(update => update.Contents).ToList();
        contents.OfType<ModelReasoning>().Should().ContainSingle().Which.ProtectedData.Should().Be(Signature);
        contents.OfType<ModelTokenUsage>().Should().ContainSingle().Which.Should().BeEquivalentTo(new ModelTokenUsage(5, 5, 10) { ReasoningTokens = 3 });
        updates.Select(update => update.FinishReason).Where(reason => reason is not null).Should().Equal("stop");
    }

    [Fact]
    public async Task A_failure_inside_a_stream_is_mapped_and_does_not_repeat_the_providers_words()
    {
        var script = new GeminiScript(ScriptedAnswer.Stream(
            ScriptedAnswer.Chunk([Text("Hi")], finish: null),
            new { error = new { code = 429, message = "Quota used by the secret prompt text", status = "RESOURCE_EXHAUSTED" } }));

        var failure = await FluentActions.Awaiting(() => Drain(script.Client().GetStreamingResponse(Prompt("Hi."), Token))).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        failure.Which.Message.Should().NotContain("secret prompt");
    }

    #endregion

    #region Models

    [Fact]
    public async Task Lists_every_page_and_only_models_that_generate_content_with_no_prices()
    {
        var script = new GeminiScript(ModelListAnswer("next"), ModelListAnswer());

        var models = await script.Provider.ListModels(GeminiScript.Connection, script.Key, Token);

        script.Sent.Should().HaveCount(2);
        script.Sent[0].Uri.Query.Should().Contain("pageSize=1000").And.NotContain("pageToken");
        script.Sent[1].Uri.Query.Should().Contain("pageToken=next");
        models.Select(model => model.Id).Should().Equal(GeminiScript.Model, "gemini-test-plain", GeminiScript.Model, "gemini-test-plain");
        var thinker = models[0];
        thinker.DisplayName.Should().Be("Gemini Test Thinker");
        thinker.Capabilities.Should().BeEquivalentTo(new ModelCapabilities
        {
            ContextTokens = 1048576,
            MaxOutputTokens = 65536,
            ToolCalling = true,
            Streaming = true,
            Vision = true,
            StructuredOutput = true,
            Reasoning = true,
            ReasoningEfforts = ["low", "high"],
        });
        thinker.Pricing.Should().Be(new ModelPricing());
        models[1].Capabilities.Reasoning.Should().BeFalse();
        models[1].Capabilities.ReasoningEfforts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_list_with_more_models_than_the_limit_is_refused()
    {
        var script = new GeminiScript(ModelListAnswer());
        var connection = GeminiScript.Connection with { Limits = new DiscoveryLimits(1024 * 1024, 1) };

        var failure = await FluentActions.Awaiting(() => script.Provider.ListModels(connection, script.Key, Token)).Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.Rejected);
    }

    [Fact]
    public async Task Testing_the_connection_reads_one_page_with_the_key_and_without_a_key_asks_for_one()
    {
        var script = new GeminiScript(ModelListAnswer());

        var result = await script.Provider.TestConnection(GeminiScript.Connection, script.Key, Token);
        var without = await script.Provider.TestConnection(GeminiScript.Connection, null, Token);

        result.Should().Be(ConnectionTestResult.Ok("Connected to Gemini."));
        without.Should().Be(ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste a Gemini API key to finish setup."));
        script.Sent.Should().HaveCount(1);
    }

    #endregion

    #region Helpers

    private static readonly string[] generateContent = ["generateContent"];

    private static readonly string[] embedContent = ["embedContent"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static bool HasId(JsonElement element) => element.TryGetProperty("id", out _);

    private static object Text(string text) => new { text };

    private static object Thought(string text) => new { text, thought = true };

    private static object Call(string? id, string city, string? signature) => new
    {
        functionCall = new { id, name = "get_weather", args = new { city } },
        thoughtSignature = signature,
    };

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        await foreach (var update in updates)
        {
            update.Contents.Should().NotBeNull();
        }
    }

    private static ScriptedAnswer ModelListAnswer(string? next = null) => ScriptedAnswer.Json(new
    {
        models = new object[]
        {
            new
            {
                name = $"models/{GeminiScript.Model}",
                displayName = "Gemini Test Thinker",
                inputTokenLimit = 1048576,
                outputTokenLimit = 65536,
                supportedGenerationMethods = generateContent,
                thinking = true,
            },
            new
            {
                name = "models/gemini-test-embedding",
                inputTokenLimit = 2048,
                outputTokenLimit = 1,
                supportedGenerationMethods = embedContent,
            },
            new
            {
                name = "models/gemini-test-plain",
                inputTokenLimit = 1000,
                outputTokenLimit = 100,
                supportedGenerationMethods = generateContent,
            },
        },
        nextPageToken = next,
    });

    #endregion
}
