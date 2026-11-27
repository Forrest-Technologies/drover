namespace Drover.Adapters.Tests;

/// <summary>What is particular to the generic compatible adapter: an optional key, an optional model list.</summary>
public sealed class OpenAICompatibleTests
{
    #region Models

    [Fact]
    public async Task Claims_only_tools_for_a_model_nobody_described()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.ListModels);

        var models = await rig.Subject.Provider.ListModels(
            rig.Subject.Connection,
            null,
            TestContext.Current.CancellationToken);

        models.Select(model => model.Id).Should().Equal("local-model", "second-model");
        models.Should().OnlyContain(model => model.Capabilities == new ModelCapabilities());
        new ModelCapabilities().Should().BeEquivalentTo(new ModelCapabilities { ToolCalling = true });
    }

    [Fact]
    public async Task Reads_a_routed_list_by_its_shape_and_a_plain_one_as_before()
    {
        var rig = CompatibleRig.Start("list_models_routed");

        var models = await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        var vision = models.Single(model => model.Id == "vendor/routed-vision");
        vision.Capabilities.Should().Match<ModelCapabilities>(can =>
            can.ContextTokens == 200_000 && can.MaxOutputTokens == 64_000 && can.Vision && can.ToolCalling && can.Reasoning && can.StructuredOutput);
        vision.Capabilities.ReasoningEfforts.Should().Equal("low", "medium", "high");
        models.Single(model => model.Id == "vendor/routed-text").Capabilities.ReasoningEfforts.Should().BeEmpty();
        vision.Pricing.Should().Be(new ModelPricing { InputPerMillion = 3m, OutputPerMillion = 15m, CachedInputPerMillion = 0.3m });
        var text = models.Single(model => model.Id == "vendor/routed-text");
        text.Capabilities.Should().Match<ModelCapabilities>(can => can.ContextTokens == 8192 && can.MaxOutputTokens == null && !can.Vision && !can.ToolCalling);
        text.Pricing.Should().Be(new ModelPricing(), "a negative price and one that is not a number stay unknown");
        models.Single(model => model.Id == "served-model").Capabilities.ContextTokens.Should().Be(32_768);
        models.Single(model => model.Id == "bare-model").Capabilities.Should().Be(new ModelCapabilities());
    }

    [Fact]
    public async Task Probes_a_server_that_has_no_model_details_once_and_never_again()
    {
        var rig = CompatibleRig.Start("list_models");

        var models = await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        models.Should().OnlyContain(model => model.Capabilities == new ModelCapabilities() && model.FactsRead);
        rig.Handler.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}")
            .Should().Equal("GET /v1/models", "POST /api/show");
        rig.Handler.Requests[1].Uri.Should().Be(new Uri("https://models.example.test/api/show"), "the probe goes to the origin, not under /v1");
    }

    [Fact]
    public async Task Fills_abilities_and_context_from_a_server_that_answers_model_details()
    {
        var rig = CompatibleRig.Start("show_model");

        var models = await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        models.Should().HaveCount(3);
        models.Should().OnlyContain(model =>
            model.Capabilities.ToolCalling && model.Capabilities.Reasoning && !model.Capabilities.Vision && model.Capabilities.ContextTokens == 131_072);
        models.Should().OnlyContain(model => model.Capabilities.ReasoningEfforts.SequenceEqual(ReasoningLevels.Documented));
        rig.Handler.Requests.Count(request => request.Uri.AbsolutePath == "/api/show").Should().Be(3);
    }

    [Fact]
    public async Task Reads_each_model_by_name_and_keeps_the_list_for_a_model_whose_details_fail()
    {
        var calls = new ConcurrentBag<string>();
        var handler = new ScriptedHandler(async (request, _, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return ScriptedHandler.Json(
                    HttpStatusCode.OK,
                    """{"data":[{"id":"a"},{"id":"b"},{"id":"c"},{"id":"d"},{"id":"e"},{"id":"f"}]}""");
            }

            var model = await ModelNamed(request, token);
            calls.Add(model);

            return model switch
            {
                "c" => ScriptedHandler.Json(HttpStatusCode.InternalServerError, "{}"),
                "d" => ScriptedHandler.Json(HttpStatusCode.OK, "not json"),
                "e" => ScriptedHandler.Json(HttpStatusCode.OK, """{"capabilities":["completion","vision"],"model_info":{"x.context_length":4096}}"""),
                _ => ScriptedHandler.Json(HttpStatusCode.OK, """{"capabilities":["completion","tools"]}"""),
            };
        });
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address);

        var models = await provider.ListModels(endpoint, null, TestContext.Current.CancellationToken);

        calls.Order().Should().Equal("a", "b", "c", "d", "e", "f");
        models.Single(model => model.Id == "a").Capabilities.Should().Match<ModelCapabilities>(can => can.ToolCalling && !can.Vision);
        models.Single(model => model.Id == "e").Capabilities.Should().Match<ModelCapabilities>(can => !can.ToolCalling && can.Vision && can.ContextTokens == 4096);
        models.Single(model => model.Id == "c").FactsRead.Should().BeFalse("its details did not come, so the stored facts stay");
        models.Single(model => model.Id == "d").FactsRead.Should().BeFalse();
        models.Single(model => model.Id == "a").FactsRead.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_first_probe_that_could_not_answer_leaves_a_plain_models_stored_facts_alone(HttpStatusCode status)
    {
        var handler = new ScriptedHandler((request, _, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? ScriptedHandler.Json(HttpStatusCode.OK, """{"data":[{"id":"plain"},{"id":"listed","context_length":4096}]}""")
            : ScriptedHandler.Json(status, "{}")));
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address);

        var models = await provider.ListModels(endpoint, null, TestContext.Current.CancellationToken);

        models.Single(model => model.Id == "plain").FactsRead.Should().BeFalse("nothing is known, so what is stored stays");
        models.Single(model => model.Id == "listed").FactsRead.Should().BeTrue("the list said something, and it is the newest word");
    }

    [Fact]
    public async Task A_first_probe_that_fails_to_connect_is_not_taken_for_a_server_of_another_kind()
    {
        var handler = new ScriptedHandler((request, _, _) => request.Method == HttpMethod.Get
            ? Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, """{"data":[{"id":"plain"}]}"""))
            : throw new HttpRequestException("reset"));
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address);

        var models = await provider.ListModels(endpoint, null, TestContext.Current.CancellationToken);

        models.Single().FactsRead.Should().BeFalse();
    }

    [Fact]
    public async Task Asks_for_the_models_whose_facts_are_not_stored_before_the_others()
    {
        var order = new ConcurrentQueue<string>();
        var handler = new ScriptedHandler(async (request, _, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return ScriptedHandler.Json(HttpStatusCode.OK, """{"data":[{"id":"stored"},{"id":"new"}]}""");
            }

            order.Enqueue(await ModelNamed(request, token));

            return ScriptedHandler.Json(HttpStatusCode.OK, """{"capabilities":["completion"]}""");
        });
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address) { Filled = new HashSet<string> { "stored" } };

        await provider.ListModels(endpoint, null, TestContext.Current.CancellationToken);

        order.First().Should().Be("new");
    }

    [Fact]
    public async Task Lists_no_models_when_the_server_has_no_model_list()
    {
        var rig = CompatibleRig.Start("no_model_list");

        var models = await rig.Subject.Provider.ListModels(
            rig.Subject.Connection,
            null,
            TestContext.Current.CancellationToken);
        var result = await rig.Subject.Provider.TestConnection(
            rig.Subject.Connection,
            null,
            TestContext.Current.CancellationToken);

        models.Should().BeEmpty();
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("does not list models");
    }

    [Fact]
    public async Task Reports_a_server_that_fails_as_unreachable()
    {
        var rig = CompatibleRig.Start("server_error");

        var result = await rig.Subject.Provider.TestConnection(
            rig.Subject.Connection,
            null,
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ModelFailure.Unreachable);
    }

    #endregion

    #region Private Methods

    private static async Task<string> ModelNamed(HttpRequestMessage request, CancellationToken token) =>
        JsonElement.Parse(await request.Content!.ReadAsStringAsync(token)).GetProperty("model").GetString()!;

    #endregion

    #region Keys

    [Fact]
    public async Task Works_without_a_key_for_local_servers()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.ListModels);

        await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        rig.Handler.Requests.Should().OnlyContain(request => !request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Sends_a_given_key_as_a_bearer_token()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);

        await rig.Subject.CreateChatClient().Ask("Say hello.");

        rig.Handler.Requests.Single().Headers["Authorization"].Should().Be($"Bearer {rig.Subject.ApiKey}");
    }

    [Fact]
    public async Task Still_builds_a_client_when_there_is_no_key()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        var client = rig.Subject.Provider.CreateChatClient(
            rig.Subject.Connection,
            null,
            rig.Subject.ModelId,
            new ModelSettings());

        var response = await client.Ask("Say hello.");

        response.Text.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Azure

    // Azure's v1 address (https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) needs no query.
    private static readonly Uri azureAddress = new("https://example.openai.azure.com/openai/v1/");

    [Fact]
    public async Task An_azure_v1_address_sends_chat_and_the_model_list_to_its_paths_with_no_query()
    {
        var chat = CompatibleRig.Start(ConformanceScenarios.PlainReply, azureAddress);
        await chat.Subject.CreateChatClient().Ask("Say hello.");
        var list = CompatibleRig.Start(ConformanceScenarios.ListModels, azureAddress);
        await list.Subject.Provider.ListModels(list.Subject.Connection, list.Subject.ApiKey, TestContext.Current.CancellationToken);

        chat.Handler.Requests.Single().Uri.Should().Be(new Uri("https://example.openai.azure.com/openai/v1/chat/completions"));
        list.Handler.Requests.Single().Uri.Should().Be(new Uri("https://example.openai.azure.com/openai/v1/models"));
    }

    [Theory]
    [InlineData("https://example.openai.azure.com/openai/v1/")]
    [InlineData("https://example.services.ai.azure.com/openai/v1/")]
    public async Task Azure_hosts_get_the_key_as_an_api_key_header_and_as_a_bearer_token(string address)
    {
        var chat = CompatibleRig.Start(ConformanceScenarios.PlainReply, new Uri(address));
        await chat.Subject.CreateChatClient().Ask("Say hello.");
        var list = CompatibleRig.Start(ConformanceScenarios.ListModels, new Uri(address));
        await list.Subject.Provider.ListModels(list.Subject.Connection, list.Subject.ApiKey, TestContext.Current.CancellationToken);

        chat.Handler.Requests.Single().Headers["api-key"].Should().Be(chat.Subject.ApiKey);
        chat.Handler.Requests.Single().Headers["Authorization"].Should().Be($"Bearer {chat.Subject.ApiKey}");
        list.Handler.Requests.Single().Headers["api-key"].Should().Be(list.Subject.ApiKey);
        list.Handler.Requests.Single().Headers["Authorization"].Should().Be($"Bearer {list.Subject.ApiKey}");
    }

    [Fact]
    public async Task Another_host_gets_no_api_key_header_even_when_its_name_looks_alike()
    {
        var chat = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        await chat.Subject.CreateChatClient().Ask("Say hello.");
        var lookalike = CompatibleRig.Start(ConformanceScenarios.ListModels, new Uri("https://openai.azure.com.example.test/v1/"));
        await lookalike.Subject.Provider.ListModels(lookalike.Subject.Connection, lookalike.Subject.ApiKey, TestContext.Current.CancellationToken);

        chat.Handler.Requests.Single().Headers.Should().NotContainKey("api-key");
        lookalike.Handler.Requests.Single().Headers.Should().NotContainKey("api-key");
    }

    [Fact]
    public async Task The_test_on_an_azure_host_tells_the_person_to_type_the_deployment_name()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.ListModels, azureAddress);
        var other = CompatibleRig.Start(ConformanceScenarios.ListModels);

        var result = await rig.Subject.Provider.TestConnection(rig.Subject.Connection, rig.Subject.ApiKey, TestContext.Current.CancellationToken);
        var plain = await other.Subject.Provider.TestConnection(other.Subject.Connection, other.Subject.ApiKey, TestContext.Current.CancellationToken);

        result.Message.Should().Contain("Type your deployment name as the model.");
        plain.Message.Should().NotContain("deployment");
    }

    [Fact]
    public void The_address_field_says_what_to_paste_for_azure()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.ListModels);

        rig.Subject.Provider.Descriptor.AddressHint.Should().Contain("For Azure OpenAI, paste your resource's endpoint.");
    }

    #endregion

    #region Wire

    [Fact]
    public async Task Calls_the_address_on_the_connection_whether_or_not_it_ends_with_a_slash()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);

        await rig.Subject.CreateChatClient().Ask("Say hello.");

        rig.Handler.Requests.Single().Uri.Should().Be(new Uri("https://models.example.test/v1/chat/completions"));
    }

    [Fact]
    public async Task A_tools_picture_goes_out_as_an_image_url_data_uri_in_a_user_message_after_the_tool_result()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "Look at the screen."),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "computer_screenshot", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", JsonElement.Parse("""{"screenshot":"attached","width":1024,"height":768}"""))]),
            new(ChatRole.User, [new TextContent("The image from the computer_screenshot call c1."), new DataContent(picture, "image/png")]),
        ];

        await rig.Subject.CreateChatClient().Ask(conversation);

        var messages = rig.Handler.Requests.Single().Json.GetProperty("messages").EnumerateArray().ToList();
        messages[2].GetProperty("role").GetString().Should().Be("tool");
        messages[2].GetProperty("content").GetString().Should().Contain("attached", "a tool result stays text");
        var shown = messages[3];
        shown.GetProperty("role").GetString().Should().Be("user");
        var parts = shown.GetProperty("content").EnumerateArray().ToList();
        parts.Select(part => part.GetProperty("type").GetString()).Should().Equal("text", "image_url");
        parts[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be($"data:image/png;base64,{Convert.ToBase64String(picture)}");
    }

    [Fact]
    public async Task A_screen_readers_request_goes_out_as_the_instruction_then_one_user_message_with_the_picture_and_no_tools()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 4, 2];
        var request = new ModelChatRequest(
            [new ModelChatMessage("user", [new ModelText("The screenshot is of a screen 1024 by 768 pixels."), new ModelData(picture, "image/png")])],
            new ModelChatOptions { Instructions = "Describe the screen.", MaxOutputTokens = 1_000 });

        await rig.Subject.CreateChatClient().GetResponse(request, TestContext.Current.CancellationToken);

        var sent = rig.Handler.Requests.Single().Json;
        var messages = sent.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal("system", "user");
        messages[0].GetProperty("content").GetString().Should().Be("Describe the screen.");
        var parts = messages[1].GetProperty("content").EnumerateArray().ToList();
        parts.Select(part => part.GetProperty("type").GetString()).Should().Equal("text", "image_url");
        parts[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be($"data:image/png;base64,{Convert.ToBase64String(picture)}");
        sent.TryGetProperty("tools", out _).Should().BeFalse("the reader is given no tools");
        (sent.TryGetProperty("max_completion_tokens", out var most) || sent.TryGetProperty("max_tokens", out most)).Should().BeTrue();
        most.GetInt32().Should().Be(1_000);
    }

    [Fact]
    public async Task Sends_the_model_name_as_given_with_no_provider_options()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);

        await rig.Subject.CreateChatClient().Ask("Say hello.");

        rig.Handler.Requests.Single().Json.GetProperty("model").GetString().Should().Be("local-model");
    }

    [Fact]
    public async Task Tells_the_caller_how_long_to_wait_from_the_retry_after_header()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.RateLimited);

        var failure = await FluentActions
            .Awaiting(() => rig.Subject.CreateChatClient().Ask("Say hello."))
            .Should().ThrowAsync<ModelProviderException>();

        failure.Which.Failure.Should().Be(ModelFailure.RetryLater);
        failure.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    #endregion
}
