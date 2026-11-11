namespace Drover.Adapters.Tests;

/// <summary>What the Venice adapter puts on the wire: the model suffix, the key, and the reasoning setting.</summary>
public sealed class VeniceRequestTests
{
    #region System Prompt

    [Fact]
    public async Task Switches_off_the_providers_system_prompt_on_every_chat_request()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.ToolCall);
        var client = rig.Subject.CreateChatClient();
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "Sunny", "get_weather")] };

        await client.Ask(ModelProviderConformance.WeatherPrompt, options);

        var chatRequests = rig.Handler.Requests.Where(IsChat).ToList();
        chatRequests.Should().NotBeEmpty();
        chatRequests.Should().OnlyContain(request =>
            request.Json.GetProperty("model").GetString() == "kimi-k3:include_venice_system_prompt=false");
    }

    [Fact]
    public async Task Switches_off_the_providers_system_prompt_when_streaming()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.Streaming);

        await foreach (var update in rig.Subject.CreateChatClient().Stream("Say hello."))
        {
            update.Contents.Should().NotBeNull();
        }

        rig.Handler.Requests.Single(IsChat).Json.GetProperty("model").GetString()
            .Should().Be("kimi-k3:include_venice_system_prompt=false");
    }

    [Fact]
    public async Task Prepends_nothing_to_the_bots_own_instructions()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);
        List<ChatMessage> conversation =
        [
            new(ChatRole.System, "You are a careful assistant."),
            new(ChatRole.User, "Say hello."),
        ];

        await rig.Subject.CreateChatClient().Ask(conversation);

        var messages = rig.Handler.Requests.Single(IsChat).Json.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal("system", "user");
        messages[0].GetProperty("content").GetString().Should().Be("You are a careful assistant.");
    }

    #endregion

    #region Provider Options

    [Theory]
    [InlineData(WebSearchMode.Off, "kimi-k3:include_venice_system_prompt=false")]
    [InlineData(WebSearchMode.On, "kimi-k3:include_venice_system_prompt=false&enable_web_search=on")]
    [InlineData(WebSearchMode.Auto, "kimi-k3:include_venice_system_prompt=false&enable_web_search=auto")]
    public async Task Sends_the_bots_web_search_choice_in_the_model_suffix(WebSearchMode mode, string expectedModel)
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);
        var client = rig.Subject.Provider.CreateChatClient(
            rig.Subject.Connection,
            rig.Subject.ApiKey,
            rig.Subject.ModelId,
            new ModelSettings(mode));

        await client.Ask("Say hello.");

        rig.Handler.Requests.Single(IsChat).Json.GetProperty("model").GetString().Should().Be(expectedModel);
    }

    [Fact]
    public async Task Turns_the_neutral_setting_none_into_the_documented_thinking_switch()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } };

        await rig.Subject.CreateChatClient().Ask("Say hello.", options);

        var body = rig.Handler.Requests.Single(IsChat).Json;
        body.GetProperty("model").GetString().Should().Be("kimi-k3:include_venice_system_prompt=false&disable_thinking=true");
        body.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.ExtraHigh, "xhigh")]
    public async Task Maps_the_neutral_effort_to_the_providers_effort_field(ReasoningEffort effort, string expected)
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = effort } };

        await rig.Subject.CreateChatClient().Ask("Say hello.", options);

        var body = rig.Handler.Requests.Single(IsChat).Json;
        body.GetProperty("reasoning_effort").GetString().Should().Be(expected);
        body.GetProperty("model").GetString().Should().NotContain("disable_thinking");
    }

    #endregion

    #region Keys

    [Fact]
    public async Task Sends_the_key_as_a_bearer_token_on_chat()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);

        await rig.Subject.CreateChatClient().Ask("Say hello.");

        rig.Handler.Requests.Single(IsChat).Headers["Authorization"].Should().Be($"Bearer {rig.Subject.ApiKey}");
    }

    [Fact]
    public async Task Lists_models_without_sending_a_key()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.ListModels);

        await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        rig.Handler.Requests.Should().OnlyContain(request => !request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public void Refuses_to_build_a_chat_client_without_a_key()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);

        var build = () => rig.Subject.Provider.CreateChatClient(
            rig.Subject.Connection,
            null,
            rig.Subject.ModelId,
            new ModelSettings());

        build.Should().Throw<ModelProviderException>().Which.Failure.Should().Be(ModelFailure.InvalidKey);
    }

    [Fact]
    public async Task Ignores_any_address_on_the_connection_because_the_address_is_fixed()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);
        var elsewhere = new ModelConnection(VeniceProvider.ConnectionKind, new Uri("https://elsewhere.example.test/v1"));

        await rig.Subject.Provider
            .CreateChatClient(elsewhere, rig.Subject.ApiKey, rig.Subject.ModelId, new ModelSettings())
            .Ask("Say hello.");

        rig.Handler.Requests.Single().Uri.Host.Should().Be("api.venice.ai");
    }

    #endregion

    #region Helpers

    private static bool IsChat(SentRequest request) => request.Uri.AbsolutePath.EndsWith("/chat/completions");

    #endregion
}
