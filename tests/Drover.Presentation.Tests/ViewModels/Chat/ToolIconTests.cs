namespace Drover.Presentation.Tests.ViewModels.Chat;

public sealed class ToolIconTests
{
    [Theory]
    [InlineData("web.search", IconName.Search, "DvHue8")]
    [InlineData("web_search", IconName.Search, "DvHue8")]
    [InlineData("web.fetch", IconName.ExternalLink, "DvHue6")]
    [InlineData("web_fetch", IconName.ExternalLink, "DvHue6")]
    [InlineData("sandbox_run", IconName.Command, "DvText3")]
    [InlineData("external.web_search", IconName.Tool, "DvText3")]
    [InlineData("", IconName.Tool, "DvText3")]
    public void A_tool_card_uses_the_icon_for_its_stored_or_live_tool_name(
        string name, IconName shape, string color)
    {
        var call = new ToolCallView(Guid.Empty, name, name, "{}", "allow", "completed", false).ToCard();

        ToolIcons.For(call.Tool).Should().Be(new ToolIcon(shape, color));
    }
}
