namespace Drover.Ui.Tests;

public sealed class DialogTests : BunitContext
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Components/Dialog.razor.js";

    private readonly BunitJSModuleInterop module;

    #endregion

    #region Constructors

    public DialogTests()
    {
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    #endregion

    #region Public Methods

    [Fact]
    public void A_dialog_is_a_native_dialog_named_by_its_title()
    {
        var dialog = RenderDialog(open: true).Find("dialog");

        var heading = dialog.QuerySelector("h2");
        heading.Should().NotBeNull();
        heading.TextContent.Should().Be("Remove bot");
        dialog.GetAttribute("aria-labelledby").Should().Be(heading.Id).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void A_closed_dialog_is_not_shown_and_renders_no_content()
    {
        var dialog = RenderDialog(open: false);

        module.Invocations.Should().BeEmpty();
        dialog.Find("dialog").HasAttribute("open").Should().BeFalse();
        dialog.FindAll(".dv-dialog-body").Should().BeEmpty();
    }

    [Fact]
    public void Opening_shows_the_dialog_modally_through_the_script()
    {
        var dialog = RenderDialog(open: true);

        module.VerifyInvoke("open").Arguments[0].ShouldBeElementReferenceTo(dialog.Find("dialog"));
    }

    [Fact]
    public void Setting_open_to_false_closes_the_dialog_through_the_script()
    {
        var dialog = RenderDialog(open: true);

        dialog.Render(parameters => parameters.Add(component => component.Open, false));

        module.VerifyInvoke("close");
    }

    [Fact]
    public async Task Escape_closes_natively_and_the_close_event_syncs_open_back_to_false()
    {
        bool? open = null;
        var dialog = Render<Dialog>(parameters => parameters
            .Add(component => component.Title, "Remove bot")
            .Add(component => component.Open, true)
            .Add(component => component.OpenChanged, value => open = value));

        await dialog.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);

        open.Should().BeFalse();
    }

    [Fact]
    public async Task A_dialog_the_browser_already_closed_is_not_closed_again()
    {
        var dialog = RenderDialog(open: true);

        await dialog.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);
        dialog.Render(parameters => parameters.Add(component => component.Open, false));

        module.Invocations.Select(call => call.Identifier).Should().Equal("open");
    }

    [Fact]
    public async Task A_dialog_closed_with_Escape_stays_closed_until_open_changes_again()
    {
        var dialog = RenderDialog(open: true);

        await dialog.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);
        dialog.Render();
        module.Invocations.Select(call => call.Identifier).Should().Equal("open");

        dialog.Render(parameters => parameters.Add(component => component.Open, false));
        dialog.Render(parameters => parameters.Add(component => component.Open, true));

        module.Invocations.Select(call => call.Identifier).Should().Equal("open", "open");
    }

    [Fact]
    public void The_close_button_is_a_real_button_that_closes_through_the_script()
    {
        var dialog = RenderDialog(open: true);

        var close = dialog.Find("header button");
        close.TextContent.Should().Be("Close");
        close.GetAttribute("type").Should().Be("button");
        close.Click();

        module.VerifyInvoke("close");
    }

    [Fact]
    public void The_same_dialog_element_is_kept_while_it_opens_and_closes_so_focus_can_return()
    {
        var dialog = RenderDialog(open: false);

        dialog.Render(parameters => parameters.Add(component => component.Open, true));
        dialog.Render(parameters => parameters.Add(component => component.Open, false));

        module.Invocations.Select(call => call.Identifier).Should().Equal("open", "close");
        module.Invocations.Select(call => call.Arguments[0]).Should().AllBeOfType<ElementReference>()
            .Which.Select(reference => reference.Id).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Removing_an_open_dialog_closes_it_first_so_focus_returns()
    {
        var dialog = RenderDialog(open: true);

        await dialog.Instance.DisposeAsync();

        module.VerifyInvoke("close");
    }

    [Fact]
    public void Actions_render_in_a_footer_only_when_given()
    {
        var plain = RenderDialog(open: true);
        var withActions = Render<Dialog>(parameters => parameters
            .Add(component => component.Title, "Remove bot")
            .Add(component => component.Open, true)
            .Add(component => component.Actions, "<button type=\"button\">Remove bot</button>"));

        plain.FindAll("footer").Should().BeEmpty();
        withActions.Find("footer button").TextContent.Should().Be("Remove bot");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Dialog> RenderDialog(bool open) =>
        Render<Dialog>(parameters => parameters
            .Add(component => component.Title, "Remove bot")
            .Add(component => component.Open, open)
            .AddChildContent("This removes the bot and its runs."));

    #endregion
}
