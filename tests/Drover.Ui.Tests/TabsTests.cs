namespace Drover.Ui.Tests;

public sealed class TabsTests : BunitContext
{
    #region Private Fields

    private static readonly TabItem[] items = [new("model", "Model"), new("tools", "Tools"), new("memory", "Memory")];

    #endregion

    #region Public Methods

    [Fact]
    public void Tabs_expose_a_named_tab_list_with_the_first_tab_showing()
    {
        var tabs = RenderTabs();
        var buttons = tabs.FindAll("[role=tab]");
        var panel = tabs.Find("[role=tabpanel]");

        tabs.Find("[role=tablist]").GetAttribute("aria-label").Should().Be("Bot settings");
        buttons.Select(button => button.GetAttribute("aria-selected")).Should().Equal("true", "false", "false");
        buttons.Select(button => button.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1");
        panel.GetAttribute("aria-labelledby").Should().Be(buttons[0].Id);
        buttons[0].GetAttribute("aria-controls").Should().Be(panel.Id);
        panel.TextContent.Trim().Should().Be("Panel for Model");
    }

    [Fact]
    public void Clicking_a_tab_shows_its_panel()
    {
        var tabs = RenderTabs();

        tabs.FindAll("[role=tab]")[1].Click();

        tabs.Find("[role=tabpanel]").TextContent.Trim().Should().Be("Panel for Tools");
        tabs.FindAll("[role=tab]")[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Theory]
    [InlineData("ArrowRight", "Tools")]
    [InlineData("ArrowLeft", "Memory")]
    [InlineData("End", "Memory")]
    public void Arrow_keys_move_between_tabs_and_wrap(string key, string expected)
    {
        var tabs = RenderTabs();

        tabs.Find("[role=tablist]").KeyDown(key);

        tabs.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void Home_returns_to_the_first_tab()
    {
        var tabs = RenderTabs();
        tabs.FindAll("[role=tab]")[2].Click();

        tabs.Find("[role=tablist]").KeyDown("Home");

        tabs.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be("Model");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Tabs> RenderTabs()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<Tabs>(parameters => parameters
            .Add(component => component.Label, "Bot settings")
            .Add(component => component.Items, items)
            .Add(component => component.ChildContent, item => $"Panel for {item.Label}"));
    }

    #endregion
}
