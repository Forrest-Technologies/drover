namespace Drover.Adapters.Tests;

public sealed class CopilotHeadersTests
{
    #region Private Fields

    private static readonly CopilotPlanOptions options = new();

    #endregion

    #region Headers

    [Fact]
    public void GitHub_login_gets_only_the_user_agent()
    {
        var headers = CopilotHeaders.For(new Uri("https://github.com/login/device/code"), options, null);

        headers.Select(header => header.Name).Should().Equal("User-Agent");
        headers[0].Value.Should().Be(options.UserAgent);
    }

    [Fact]
    public void The_models_list_carries_the_editor_headers_and_the_api_version()
    {
        var headers = CopilotHeaders.For(new Uri("https://api.individual.githubcopilot.com/models"), options, null);

        headers.Select(header => header.Name).Should().Contain(["User-Agent", "Editor-Version", "Editor-Plugin-Version", "Copilot-Integration-Id", "X-GitHub-Api-Version"]);
        headers.Should().NotContain(header => header.Name == "X-Initiator");
    }

    [Fact]
    public void A_chat_from_the_person_with_a_picture_sets_initiator_intent_and_vision()
    {
        var headers = CopilotHeaders.For(
            new Uri("https://api.individual.githubcopilot.com/chat/completions"),
            options,
            new CopilotCall(FromPerson: true, HasImage: true));

        headers.Should().Contain(("X-Initiator", "user"));
        headers.Should().Contain(("Openai-Intent", "conversation-edits"));
        headers.Should().Contain(("Copilot-Vision-Request", "true"));
    }

    [Fact]
    public void A_follow_up_after_a_tool_is_from_the_agent_and_does_not_claim_vision()
    {
        var headers = CopilotHeaders.For(
            new Uri("https://api.individual.githubcopilot.com/chat/completions"),
            options,
            new CopilotCall(FromPerson: false, HasImage: false));

        headers.Should().Contain(("X-Initiator", "agent"));
        headers.Should().NotContain(header => header.Name == "Copilot-Vision-Request");
    }

    [Fact]
    public void The_api_host_is_read_from_proxy_ep_in_the_session_token()
    {
        CopilotApiAddress.FromToken("tid=x;exp=1;proxy-ep=proxy.individual.githubcopilot.com;sku=1")
            .Should().Be(new Uri("https://api.individual.githubcopilot.com/"));
        CopilotApiAddress.FromToken("no-claim").Should().BeNull();
    }

    [Fact]
    public void A_tool_object_without_additional_properties_is_closed()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""");

        var tightened = CopilotToolSchema.Tighten(schema.RootElement);

        tightened.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        tightened.GetProperty("properties").GetProperty("city").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void Nested_objects_and_array_items_are_closed_too()
    {
        using var schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"place":{"type":"object","properties":{"city":{"type":"string"}}},"tags":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}
            """);

        var tightened = CopilotToolSchema.Tighten(schema.RootElement);

        tightened.GetProperty("properties").GetProperty("place").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        tightened.GetProperty("properties").GetProperty("tags").GetProperty("items").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    #endregion
}
