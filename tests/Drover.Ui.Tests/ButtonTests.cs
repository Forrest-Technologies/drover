namespace Drover.Ui.Tests;

public sealed class ButtonTests : BunitContext
{
    #region Public Methods

    [Fact]
    public void A_button_is_a_real_button_named_by_its_content()
    {
        var button = Render<Button>(parameters => parameters.AddChildContent("Connect calendar")).Find("button");

        button.TextContent.Trim().Should().Be("Connect calendar");
        button.GetAttribute("type").Should().Be("button");
    }

    [Fact]
    public void Clicking_a_button_raises_its_action()
    {
        var clicks = 0;
        var button = Render<Button>(parameters => parameters
            .Add(component => component.OnClick, () => clicks++)
            .AddChildContent("Save"));

        button.Find("button").Click();

        clicks.Should().Be(1);
    }

    [Fact]
    public void A_disabled_button_is_disabled_for_assistive_technology_too()
    {
        var button = Render<Button>(parameters => parameters
            .Add(component => component.Disabled, true)
            .AddChildContent("Save"));

        button.Find("button").HasAttribute("disabled").Should().BeTrue();
    }

    #endregion
}
