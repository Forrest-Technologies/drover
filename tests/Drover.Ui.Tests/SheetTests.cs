namespace Drover.Ui.Tests;

public sealed class SheetTests : BunitContext
{
    #region Private Fields

    private const string DialogModulePath = "./_content/Drover.Ui/Components/Dialog.razor.js";

    private readonly BunitJSModuleInterop module;

    #endregion

    #region Constructors

    public SheetTests()
    {
        module = JSInterop.SetupModule(DialogModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    #endregion

    #region Public Methods

    [Fact]
    public void A_sheet_is_a_native_dialog_named_by_its_title()
    {
        var dialog = RenderSheet(open: true).Find(".dv-sheet dialog");

        var heading = dialog.QuerySelector("h2");
        heading.Should().NotBeNull();
        heading.TextContent.Should().Be("Bot details");
        dialog.GetAttribute("aria-labelledby").Should().Be(heading.Id).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void A_sheet_opens_and_closes_through_the_dialog_script()
    {
        var sheet = RenderSheet(open: true);

        sheet.Render(parameters => parameters.Add(component => component.Open, false));

        module.Invocations.Select(call => call.Identifier).Should().Equal("open", "close");
    }

    [Fact]
    public async Task Escape_closes_natively_and_the_close_event_syncs_open_back_to_false()
    {
        bool? open = null;
        var sheet = Render<Sheet>(parameters => parameters
            .Add(component => component.Title, "Bot details")
            .Add(component => component.Open, true)
            .Add(component => component.OpenChanged, value => open = value));

        await sheet.Find("dialog").TriggerEventAsync("onclose", EventArgs.Empty);

        open.Should().BeFalse();
    }

    [Fact]
    public void A_sheet_shows_its_content_and_actions()
    {
        var sheet = Render<Sheet>(parameters => parameters
            .Add(component => component.Title, "Bot details")
            .Add(component => component.Open, true)
            .AddChildContent("Runs once a day.")
            .Add(component => component.Actions, "<button type=\"button\">Pause bot</button>"));

        sheet.Find(".dv-dialog-body").TextContent.Trim().Should().Be("Runs once a day.");
        sheet.Find("footer button").TextContent.Should().Be("Pause bot");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Sheet> RenderSheet(bool open) =>
        Render<Sheet>(parameters => parameters
            .Add(component => component.Title, "Bot details")
            .Add(component => component.Open, open)
            .AddChildContent("Runs once a day."));

    #endregion
}
