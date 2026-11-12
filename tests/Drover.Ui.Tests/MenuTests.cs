namespace Drover.Ui.Tests;

public sealed class MenuTests : BunitContext
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Components/Menu.razor.js";

    private readonly BunitJSModuleInterop module;

    private readonly List<string> selected = [];

    #endregion

    #region Constructors

    public MenuTests()
    {
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
    }

    #endregion

    #region Public Methods

    [Fact]
    public void The_trigger_is_a_real_button_named_by_the_label_that_opens_the_menu_natively()
    {
        var menu = RenderMenu();

        var trigger = menu.Find("button.dv-menu-trigger");
        var list = menu.Find("[role=menu]");
        trigger.TextContent.Should().Be("Bot actions");
        trigger.GetAttribute("type").Should().Be("button");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("popovertarget").Should().Be(list.Id).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void The_menu_is_an_auto_popover_so_Escape_and_a_click_outside_close_it_natively()
    {
        var list = RenderMenu().Find("[role=menu]");

        list.HasAttribute("popover").Should().BeTrue();
        list.GetAttribute("popover").Should().BeEmpty();
    }

    [Fact]
    public void The_menu_is_named_by_its_trigger()
    {
        var menu = RenderMenu();

        menu.Find("[role=menu]").GetAttribute("aria-labelledby").Should().Be(menu.Find("button.dv-menu-trigger").Id);
    }

    [Fact]
    public void Items_are_real_buttons_with_the_menuitem_role_in_document_order()
    {
        var items = RenderMenu().FindAll("[role=menu] > button[role=menuitem]");

        items.Select(item => item.TextContent.Trim()).Should().Equal("Pause", "Edit", "Copy link", "Remove");
        items.Should().OnlyContain(item => item.GetAttribute("type") == "button");
    }

    [Fact]
    public void A_disabled_item_is_disabled_for_assistive_technology_too()
    {
        Item(RenderMenu(), "Pause").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Opening_puts_focus_on_the_first_item_that_is_not_disabled()
    {
        var menu = RenderMenu();

        menu.FindAll("[role=menuitem][autofocus]").Select(item => item.TextContent.Trim()).Should().Equal("Edit");
    }

    [Theory]
    [InlineData("Edit", "ArrowDown", "Copy link")]
    [InlineData("Remove", "ArrowDown", "Edit")]
    [InlineData("Copy link", "ArrowUp", "Edit")]
    [InlineData("Edit", "ArrowUp", "Remove")]
    [InlineData("Remove", "Home", "Edit")]
    [InlineData("Edit", "End", "Remove")]
    public void Arrow_keys_Home_and_End_move_focus_between_enabled_items(string from, string key, string to)
    {
        var menu = RenderMenu();

        Item(menu, from).KeyDown(new KeyboardEventArgs { Key = key });

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(Item(menu, to));
    }

    [Theory]
    [InlineData("Tab")]
    [InlineData("Enter")]
    [InlineData(" ")]
    [InlineData("a")]
    public void Other_keys_are_left_to_the_browser(string key)
    {
        var menu = RenderMenu();

        Item(menu, "Edit").KeyDown(new KeyboardEventArgs { Key = key });

        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
        selected.Should().BeEmpty();
    }

    [Fact]
    public void Activating_an_item_runs_its_action_once()
    {
        var menu = RenderMenu();

        Item(menu, "Copy link").Click();

        selected.Should().Equal("Copy link");
    }

    [Fact]
    public void Activating_an_item_closes_the_menu_and_returns_focus_to_the_trigger()
    {
        var menu = RenderMenu();
        var item = Item(menu, "Edit");

        item.Click();

        item.GetAttribute("popovertarget").Should().Be(menu.Find("[role=menu]").Id);
        item.GetAttribute("popovertargetaction").Should().Be("hide");
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(menu.Find("button.dv-menu-trigger"));
    }

    [Fact]
    public void The_script_is_attached_to_the_list_and_told_which_button_is_the_trigger()
    {
        var menu = RenderMenu();

        var call = module.VerifyInvoke("attach");
        call.Arguments[0].ShouldBeElementReferenceTo(menu.Find("[role=menu]"));
        call.Arguments[1].ShouldBeElementReferenceTo(menu.Find("button.dv-menu-trigger"));
    }

    [Fact]
    public void An_item_added_later_takes_its_place_in_document_order()
    {
        var showPause = false;
        RenderFragment items = builder =>
        {
            if (showPause)
            {
                builder.OpenComponent<MenuItem>(0);
                builder.AddAttribute(1, nameof(MenuItem.ChildContent), (RenderFragment)(content => content.AddContent(0, "Pause")));
                builder.CloseComponent();
            }

            builder.OpenComponent<MenuItem>(2);
            builder.AddAttribute(3, nameof(MenuItem.ChildContent), (RenderFragment)(content => content.AddContent(0, "Remove")));
            builder.CloseComponent();
        };
        var menu = Render<Menu>(parameters => parameters
            .Add(component => component.Label, "Bot actions")
            .Add(component => component.ChildContent, items));

        showPause = true;
        menu.Render(parameters => parameters.Add(component => component.ChildContent, items));
        Item(menu, "Remove").KeyDown(new KeyboardEventArgs { Key = "Home" });

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(Item(menu, "Pause"));
    }

    [Fact]
    public void An_item_outside_a_menu_says_where_it_belongs()
    {
        var render = () => Render<MenuItem>(parameters => parameters.AddChildContent("Edit"));

        render.Should().Throw<InvalidOperationException>().WithMessage("A MenuItem belongs inside a Menu.");
    }

    #endregion

    #region Private Methods

    private static IElement Item(IRenderedComponent<Menu> menu, string text) =>
        menu.FindAll("[role=menuitem]").Single(item => item.TextContent.Trim() == text);

    private IRenderedComponent<Menu> RenderMenu() =>
        Render<Menu>(parameters => parameters
            .Add(component => component.Label, "Bot actions")
            .AddChildContent<MenuItem>(item => item
                .Add(component => component.Disabled, true)
                .Add(component => component.OnSelect, () => selected.Add("Pause"))
                .AddChildContent("Pause"))
            .AddChildContent<MenuItem>(item => item
                .Add(component => component.OnSelect, () => selected.Add("Edit"))
                .AddChildContent("Edit"))
            .AddChildContent<MenuItem>(item => item
                .Add(component => component.OnSelect, () => selected.Add("Copy link"))
                .AddChildContent("Copy link"))
            .AddChildContent<MenuItem>(item => item
                .Add(component => component.OnSelect, () => selected.Add("Remove"))
                .AddChildContent("Remove")));

    #endregion
}
