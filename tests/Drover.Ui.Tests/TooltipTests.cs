namespace Drover.Ui.Tests;

public sealed class TooltipTests : BunitContext
{
    #region Public Methods

    [Fact]
    public void A_tooltip_has_the_tooltip_role_and_describes_its_control()
    {
        var tooltip = RenderTooltip();

        var popup = tooltip.Find("[role=tooltip]");
        popup.TextContent.Trim().Should().Be("Copies the server address");
        tooltip.Find("button").GetAttribute("aria-describedby").Should().Be(popup.Id).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void Two_tooltips_do_not_share_an_id()
    {
        var first = RenderTooltip().Find("[role=tooltip]").Id;
        var second = RenderTooltip().Find("[role=tooltip]").Id;

        first.Should().NotBe(second);
    }

    [Fact]
    public void Escape_dismisses_the_tooltip_while_its_control_keeps_focus()
    {
        var tooltip = RenderTooltip();

        tooltip.Find("button").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        tooltip.Find(".dv-tooltip").ClassList.Should().Contain("dv-tooltip-dismissed");
    }

    [Fact]
    public void Other_keys_do_not_dismiss_the_tooltip()
    {
        var tooltip = RenderTooltip();

        tooltip.Find("button").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        tooltip.Find(".dv-tooltip").ClassList.Should().NotContain("dv-tooltip-dismissed");
    }

    [Fact]
    public void A_dismissed_tooltip_comes_back_after_focus_leaves()
    {
        var tooltip = RenderTooltip();
        tooltip.Find("button").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        tooltip.Find(".dv-tooltip").FocusOut();

        tooltip.Find(".dv-tooltip").ClassList.Should().NotContain("dv-tooltip-dismissed");
    }

    [Fact]
    public void A_dismissed_tooltip_comes_back_after_the_pointer_leaves()
    {
        var tooltip = RenderTooltip();
        tooltip.Find("button").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        tooltip.Find(".dv-tooltip").TriggerEvent("onpointerleave", new PointerEventArgs());

        tooltip.Find(".dv-tooltip").ClassList.Should().NotContain("dv-tooltip-dismissed");
    }

    [Fact]
    public void The_styles_show_the_tooltip_on_keyboard_focus_as_well_as_on_hover()
    {
        var styles = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "src", "Drover.Ui", "Components", "Tooltip.razor.css"));

        styles.Should().Contain(".dv-tooltip:hover > .dv-tooltip-popup");
        styles.Should().Contain(".dv-tooltip:has(:focus-visible) > .dv-tooltip-popup");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Tooltip> RenderTooltip() =>
        Render<Tooltip>(parameters => parameters
            .Add(component => component.Text, "Copies the server address")
            .Add(component => component.ChildContent, describedBy =>
                $"<button type=\"button\" aria-describedby=\"{describedBy}\">Copy</button>"));

    #endregion
}
