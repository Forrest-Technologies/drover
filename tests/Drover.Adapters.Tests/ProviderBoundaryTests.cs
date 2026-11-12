namespace Drover.Adapters.Tests;

public sealed class ProviderBoundaryTests
{
    #region Boundaries

    [Fact]
    public void Domain_has_no_vendor_dependency_and_venice_has_no_openai_dependency()
    {
        typeof(IModelChatClient).Assembly.GetReferencedAssemblies().Should().NotContain(assembly =>
            assembly.Name!.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || assembly.Name.StartsWith("OpenAI", StringComparison.Ordinal));
        typeof(VeniceProvider).Assembly.GetReferencedAssemblies().Should().NotContain(assembly =>
            assembly.Name!.StartsWith("OpenAI", StringComparison.Ordinal)
            || assembly.Name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || assembly.Name == "Drover.Adapters.OpenAICompatible");
    }

    [Fact]
    public void Public_registration_resolves_both_providers_with_the_shared_protocol()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider().AddVeniceProvider();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetServices<IModelClientProvider>().Select(value => value.Kind).Should().BeEquivalentTo("venice", "openai_compatible");
    }

    [Fact]
    public async Task Unsupported_content_fails_before_sending_a_request()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        var bridge = new ModelChatClientBridge(rig.Subject.CreateChatClient());
        var conversation = new[] { new ChatMessage(ChatRole.User, [new ErrorContent("unsupported")]) };
        await FluentActions.Awaiting(() => bridge.Ask(conversation)).Should().ThrowAsync<NotSupportedException>();
        rig.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Bridge_preserves_image_options_and_usage_without_raw_vendor_objects()
    {
        var handler = new ScriptedHandler(async (request, _, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            body.RootElement.GetProperty("temperature").GetSingle().Should().Be(0.25f);
            body.RootElement.GetProperty("max_completion_tokens").GetInt32().Should().Be(123);
            body.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("image_url").GetProperty("url").GetString().Should().Be("https://images.example.test/photo.png");
            return ScriptedHandler.Json(HttpStatusCode.OK, """{"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":7,"completion_tokens":3,"total_tokens":10,"prompt_tokens_details":{"cached_tokens":2},"completion_tokens_details":{"reasoning_tokens":1}}}""");
        });
        using var client = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler)).CreateChatClient(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address), TestKeys.Generate(), "m", new ModelSettings());
        var response = await client.Ask([new ChatMessage(ChatRole.User, [new UriContent(new Uri("https://images.example.test/photo.png"), "image/png")])], new ChatOptions { Temperature = 0.25f, MaxOutputTokens = 123 });
        response.Usage!.InputTokenCount.Should().Be(7);
        response.Usage.OutputTokenCount.Should().Be(3);
        response.Usage.TotalTokenCount.Should().Be(10);
        response.Usage.CachedInputTokenCount.Should().Be(2);
        response.Usage.ReasoningTokenCount.Should().Be(1);
        response.RawRepresentation.Should().BeNull();
        response.Messages.Should().OnlyContain(message => message.RawRepresentation == null);
    }

    [Fact]
    public async Task Sdk_pipeline_does_not_apply_a_second_network_timeout()
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(Reply(null)));
        using var httpClient = new FixtureHttpClientFactory(handler).CreateClient("timeout-proof");
        var options = ProtocolChatClient.CreateOptions(httpClient, new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address));
        var capture = new TimeoutCapture();
        options.AddPolicy(capture, PipelinePosition.PerCall);
        var client = new OpenAI.OpenAIClient(new ApiKeyCredential(TestKeys.Generate()), options).GetChatClient("m").AsIChatClient();
        await client.Ask("hello");
        capture.Value.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"city\"")]
    public async Task Non_object_tool_arguments_are_rejected_before_the_request(string arguments)
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        using var data = JsonDocument.Parse(arguments);
        var request = new ModelChatRequest([new ModelChatMessage("assistant", [new ModelToolCall("c", "weather", data.RootElement.Clone())])], new ModelChatOptions());
        await FluentActions.Awaiting(() => rig.Subject.CreateChatClient().GetResponse(request, TestContext.Current.CancellationToken)).Should().ThrowAsync<NotSupportedException>();
        rig.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Streamed_tool_argument_fragments_reach_the_framework_as_one_complete_call()
    {
        var stream = """
            data: {"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"weather","arguments":"{\"city\":"}}]}}]}

            data: {"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Paris\"}"}}]}}]}

            data: {"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(stream)))),
        }));
        using var client = VeniceClient(handler);
        var updates = await client.StreamToEnd("weather");
        var call = updates.ToChatResponse().Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
        call.CallId.Should().Be("call-1");
        call.Name.Should().Be("weather");
        call.Arguments!["city"]!.ToString().Should().Be("Paris");
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("xhigh", "xhigh")]
    public async Task Bot_reasoning_defaults_are_used_in_the_neutral_string_form(string effort, string expected)
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.PlainReply);
        using var client = rig.Subject.Provider.CreateChatClient(rig.Subject.Connection, rig.Subject.ApiKey, rig.Subject.ModelId, new ModelSettings { ReasoningEffort = effort });
        await client.Ask("hello");
        rig.Handler.Requests.Single().Json.GetProperty("reasoning_effort").GetString().Should().Be(expected);
    }

    #endregion

    #region Response Isolation

    [Fact]
    public async Task A_later_response_without_balances_does_not_reuse_an_earlier_balance()
    {
        var handler = new ScriptedHandler((_, count, _) => Task.FromResult(Reply(count == 1 ? "4.20" : null)));
        using var client = VeniceClient(handler);
        var first = await client.Ask("first");
        var second = await client.Ask("second");
        first.AdditionalProperties.Should().ContainKey(ModelResponseKeys.Balances);
        second.AdditionalProperties.Should().BeNull();
    }

    [Fact]
    public async Task Overlapping_calls_keep_their_own_balances()
    {
        var firstArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler(async (_, count, token) =>
        {
            if (count == 1)
            {
                firstArrived.SetResult();
                await secondArrived.Task.WaitAsync(token);
                return Reply("1");
            }

            secondArrived.SetResult();
            return Reply("2");
        });
        using var client = VeniceClient(handler);
        var first = client.Ask("first");
        await firstArrived.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = client.Ask("second");
        var responses = await Task.WhenAll(first, second);
        ((IReadOnlyList<ModelBalance>)responses[0].AdditionalProperties![ModelResponseKeys.Balances]!).Single().Amount.Should().Be(1);
        ((IReadOnlyList<ModelBalance>)responses[1].AdditionalProperties![ModelResponseKeys.Balances]!).Single().Amount.Should().Be(2);
    }

    #endregion

    #region Failures

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_with_a_nested_network_failure_stays_cancellation(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler(async (_, _, _) =>
        {
            await cancellation.CancelAsync();
            throw new OperationCanceledException("cancelled", new HttpRequestException("connection interrupted"), cancellation.Token);
        });
        using var client = VeniceClient(handler);
        var request = new ModelChatRequest([new ModelChatMessage("user", [new ModelText("hello")])], new ModelChatOptions());
        var act = async () =>
        {
            if (streaming)
            {
                await foreach (var update in client.GetStreamingResponse(request, cancellation.Token))
                {
                    update.Should().NotBeNull();
                }
            }
            else
            {
                await client.GetResponse(request, cancellation.Token);
            }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Network_failures_do_not_expose_response_text_or_vendor_exceptions()
    {
        var secret = TestKeys.Generate();
        var failure = new HttpRequestException(HttpRequestError.InvalidResponse, secret);
        ProtocolFailureMapping.TryMap(failure, out var compatible).Should().BeTrue();
        VeniceChatClient.TryMap(failure, out var venice).Should().BeTrue();
        foreach (var mapped in new[] { compatible, venice })
        {
            mapped.InnerException.Should().BeNull();
            mapped.ToString().Should().NotContain(secret);
        }
    }

    [Theory]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    public void Invalid_reset_ranges_are_ignored(string value) =>
        VeniceHeaders.WaitUntilReset(value, DateTimeOffset.UnixEpoch).Should().BeNull();

    [Theory]
    [InlineData("m:include_venice_system_prompt=true")]
    [InlineData("m&enable_web_search=on")]
    public void Caller_suffixes_cannot_override_provider_policy(string model) =>
        FluentActions.Invoking(() => VeniceModelSuffix.Build(model, WebSearchMode.Off, false)).Should().Throw<ArgumentException>();

    #endregion

    #region Helpers

    private sealed class TimeoutCapture : PipelinePolicy
    {
        #region Properties

        public TimeSpan? Value { get; private set; }

        #endregion

        #region Interface Implementations

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Value = message.NetworkTimeout;
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Value = message.NetworkTimeout;
            await ProcessNextAsync(message, pipeline, currentIndex);
        }

        #endregion
    }

    private static IModelChatClient VeniceClient(HttpMessageHandler handler) =>
        ProviderBuilders.Venice(new FixtureHttpClientFactory(handler)).CreateChatClient(
            new ModelEndpoint(VeniceProvider.ConnectionKind, VeniceProvider.BaseAddress), TestKeys.Generate(), "m", new ModelSettings());

    private static HttpResponseMessage Reply(string? balance)
    {
        var response = ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply);
        if (balance is not null)
        {
            response.Headers.Add("x-venice-balance-usd", balance);
        }

        return response;
    }

    #endregion
}
