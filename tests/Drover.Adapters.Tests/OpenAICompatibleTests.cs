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

    #region Keys

    [Fact]
    public async Task Works_without_a_key_for_local_servers()
    {
        var rig = CompatibleRig.Start(ConformanceScenarios.ListModels);

        await rig.Subject.Provider.ListModels(rig.Subject.Connection, null, TestContext.Current.CancellationToken);

        rig.Handler.Requests.Single().Headers.Should().NotContainKey("Authorization");
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
