namespace Drover.Engine.Tests;

/// <summary>
/// The server's validators take text as long as the shared field limit and refuse one character more, so a client
/// that stops typing at the limit never sends what the server refuses, and never stops short of what it takes.
/// </summary>
public sealed class FieldLimitsTests
{
    #region Extension Files

    [Theory]
    [InlineData("name", FieldLimits.BotName)]
    [InlineData("description", FieldLimits.BotDescription)]
    public void An_extension_field_is_taken_at_its_limit_and_refused_past_it(string field, int limit)
    {
        ReadExtension(Package(field, new string('a', limit))).Problem.Should().BeNull();
        ReadExtension(Package(field, new string('a', limit + 1))).Problem.Should().NotBeNull();
    }

    [Fact]
    public void An_extension_id_is_taken_at_its_limit_and_refused_past_it()
    {
        ReadExtension(Package("id", $"a{new string('b', ExtensionPackage.MaxIdLength - 1)}")).Problem.Should().BeNull();
        ReadExtension(Package("id", $"a{new string('b', ExtensionPackage.MaxIdLength)}")).Problem.Should().NotBeNull();
    }

    [Theory]
    [InlineData("name", FieldLimits.BotName)]
    [InlineData("description", FieldLimits.BotDescription)]
    [InlineData("suggestedModel", FieldLimits.ModelName)]
    public void A_field_of_a_bot_file_is_taken_at_its_limit_and_refused_past_it(string field, int limit)
    {
        BotFileReader.Read(JsonSerializer.Serialize(BotFile(field, new string('a', limit)))).Problem.Should().BeNull();
        BotFileReader.Read(JsonSerializer.Serialize(BotFile(field, new string('a', limit + 1)))).Problem.Should().NotBeNull();
    }

    #endregion

    #region Tool Servers

    [Fact]
    public void A_tool_server_name_is_taken_at_its_limit_and_refused_past_it()
    {
        ToolConnectionService.FindAddressProblem(new string('a', FieldLimits.ToolServerName), "https://tools.example.test/mcp")
            .Should().BeNull();
        ToolConnectionService.FindAddressProblem(new string('a', FieldLimits.ToolServerName + 1), "https://tools.example.test/mcp")
            .Should().NotBeNull();
    }

    [Fact]
    public void A_header_name_is_taken_at_its_limit_and_refused_past_it()
    {
        ToolConnectionService.FindHeaderNameProblem($"X-{new string('a', FieldLimits.HeaderName - 2)}").Should().BeNull();
        ToolConnectionService.FindHeaderNameProblem($"X-{new string('a', FieldLimits.HeaderName - 1)}").Should().NotBeNull();
    }

    #endregion

    #region Sandboxes

    [Fact]
    public void Typed_text_is_taken_at_its_limit_and_refused_past_it()
    {
        var atLimit = () => DesktopCommands.Type(new string('a', FieldLimits.TypedText));
        var pastLimit = () => DesktopCommands.Type(new string('a', FieldLimits.TypedText + 1));

        atLimit.Should().NotThrow();
        pastLimit.Should().Throw<BuiltinToolException>();
    }

    [Fact]
    public void The_server_limits_that_have_no_public_validator_are_the_shared_constants()
    {
        BotService.MaxNameLength.Should().Be(FieldLimits.BotName);
        BotService.MaxDescriptionLength.Should().Be(FieldLimits.BotDescription);
        BotService.MaxModelLength.Should().Be(FieldLimits.ModelName);
        SandboxControlService.MaxCommandCharacters.Should().Be(FieldLimits.SandboxCommand);
    }

    #endregion

    #region Private Methods

    private static ExtensionReading ReadExtension(Dictionary<string, object> package) =>
        ExtensionPackages.Read(JsonSerializer.Serialize(package));

    private static Dictionary<string, object> Package(string field, string value)
    {
        var package = new Dictionary<string, object>
        {
            ["version"] = 2,
            ["id"] = "sample.limits",
            ["name"] = "Sample",
            ["description"] = string.Empty,
            ["contents"] = new object[] { new Dictionary<string, object> { ["kind"] = "mcp_connection", ["name"] = "Helpdesk", ["url"] = "https://tools.example.test/mcp" } },
        };
        package[field] = value;
        return package;
    }

    private static Dictionary<string, object> BotFile(string field, string value)
    {
        var bot = new Dictionary<string, object> { ["version"] = 1, ["name"] = "Sample bot", ["instructions"] = string.Empty };
        bot[field] = value;
        return bot;
    }

    #endregion
}
