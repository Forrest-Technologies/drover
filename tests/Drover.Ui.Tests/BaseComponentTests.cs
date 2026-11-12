namespace Drover.Ui.Tests;

public sealed class BaseComponentTests : BunitContext
{
    #region Icon Button

    [Fact]
    public void An_icon_button_is_named_by_its_label()
    {
        var button = Render<IconButton>(parameters => parameters
            .Add(component => component.Icon, IconName.Close)
            .Add(component => component.Label, "Close panel")).Find("button");

        button.GetAttribute("aria-label").Should().Be("Close panel");
        button.QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void An_icon_button_without_a_label_is_refused()
    {
        var render = () => Render<IconButton>(parameters => parameters
            .Add(component => component.Icon, IconName.Close)
            .Add(component => component.Label, " "));

        render.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Every_icon_has_a_shape()
    {
        foreach (var name in Enum.GetValues<IconName>())
        {
            IconShapes.For(name).Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion

    #region Field And Select

    [Fact]
    public void A_field_is_labeled_and_reports_what_is_typed()
    {
        var typed = string.Empty;
        var field = Render<Field>(parameters => parameters
            .Add(component => component.Label, "Base address")
            .Add(component => component.ValueChanged, value => typed = value));
        var input = field.Find("input");

        field.Find("label").GetAttribute("for").Should().Be(input.Id);
        field.Find("label").TextContent.Should().Be("Base address");

        input.Input("https://example.org");

        typed.Should().Be("https://example.org");
    }

    [Fact]
    public void A_field_error_is_said_in_words_and_tied_to_the_input()
    {
        var field = Render<Field>(parameters => parameters
            .Add(component => component.Label, "Key")
            .Add(component => component.Hint, "Stored encrypted.")
            .Add(component => component.Error, "The key was rejected. Check it and try again."));
        var input = field.Find("input");
        var described = input.GetAttribute("aria-describedby")!.Split(' ');

        input.GetAttribute("aria-invalid").Should().Be("true");
        described.Should().HaveCount(2);
        described.Select(id => field.Find($"#{id}").TextContent).Should()
            .Equal("Stored encrypted.", "The key was rejected. Check it and try again.");
    }

    [Fact]
    public void A_select_is_a_labeled_native_select_that_reports_the_choice()
    {
        var chosen = string.Empty;
        var select = Render<Select>(parameters => parameters
            .Add(component => component.Label, "Model")
            .Add(component => component.ValueChanged, value => chosen = value)
            .AddChildContent("<option value=\"a\">Sample model A</option><option value=\"b\">Sample model B</option>"));
        var input = select.Find("select");

        select.Find("label").GetAttribute("for").Should().Be(input.Id);

        input.Change("b");

        chosen.Should().Be("b");
    }

    #endregion

    #region Switch And Segmented Control

    [Fact]
    public void A_switch_has_the_switch_role_its_label_and_toggles()
    {
        var on = false;
        var toggle = Render<Switch>(parameters => parameters
            .Add(component => component.Label, "Ask before running")
            .Add(component => component.ValueChanged, value => on = value));
        var input = toggle.Find("input");

        input.GetAttribute("role").Should().Be("switch");
        toggle.Find("label").TextContent.Trim().Should().Be("Ask before running");

        input.Change(true);

        on.Should().BeTrue();
    }

    [Fact]
    public void A_segmented_control_is_one_named_radio_group()
    {
        var chosen = "off";
        var control = Render<SegmentedControl<string>>(parameters => parameters
            .Add(component => component.Label, "Access")
            .Add(component => component.Options, [new("off", "Off"), new("ask", "Ask"), new("allow", "Allow")])
            .Add(component => component.Value, "off")
            .Add(component => component.ValueChanged, value => chosen = value));
        var radios = control.FindAll("input[type=radio]");

        control.Find("fieldset legend").TextContent.Should().Be("Access");
        radios.Should().HaveCount(3);
        radios.Select(radio => radio.GetAttribute("name")).Distinct().Should().HaveCount(1);
        radios[0].HasAttribute("checked").Should().BeTrue();

        radios[2].Change(true);

        chosen.Should().Be("allow");
    }

    #endregion

    #region Avatar And Badge

    [Fact]
    public void An_avatar_is_an_image_named_for_the_bot()
    {
        var avatar = Render<Avatar>(parameters => parameters
            .Add(component => component.Name, "Sample bot")
            .Add(component => component.Hue, 14)).Find("[role=img]");

        avatar.GetAttribute("aria-label").Should().Be("Sample bot");
        avatar.ClassList.Should().Contain("dv-hue-2");
        avatar.TextContent.Trim().Should().Be("SB");
    }

    [Fact]
    public void A_thinking_avatar_says_so_in_its_name_and_carries_the_live_state()
    {
        var avatar = Render<Avatar>(parameters => parameters
            .Add(component => component.Name, "Sample bot")
            .Add(component => component.State, AvatarState.Thinking));

        avatar.Find("[role=img]").GetAttribute("aria-label").Should().Be("Sample bot, thinking");
        avatar.Find(".dv-avatar-ring").ClassList.Should().Contain("dv-state-thinking");
    }

    [Fact]
    public void An_avatar_at_rest_carries_no_live_state()
    {
        var avatar = Render<Avatar>(parameters => parameters.Add(component => component.Name, "Sample bot"));

        avatar.Find(".dv-avatar-ring").ClassList.Should().NotContain("dv-state-thinking");
    }

    [Fact]
    public void An_avatar_image_has_dimensions()
    {
        var image = Render<Avatar>(parameters => parameters
            .Add(component => component.Name, "Sample bot")
            .Add(component => component.Size, 48)
            .Add(component => component.ImageUrl, "/files/sample.png")).Find("img");

        image.GetAttribute("width").Should().Be("48");
        image.GetAttribute("height").Should().Be("48");
    }

    [Fact]
    public void A_badge_says_its_state_in_words()
    {
        var badge = Render<Badge>(parameters => parameters
            .Add(component => component.Kind, BadgeKind.Warn)
            .AddChildContent("Waiting for you"));

        badge.Find(".dv-badge-warn").TextContent.Should().Be("Waiting for you");
    }

    #endregion

    #region Empty State

    [Fact]
    public void An_empty_state_says_why_and_offers_one_action()
    {
        var acted = false;
        var empty = Render<EmptyState>(parameters => parameters
            .Add(component => component.Cause, "No model is connected yet.")
            .Add(component => component.ActionLabel, "Connect a model")
            .Add(component => component.OnAction, () => acted = true));

        empty.Find("p").TextContent.Should().Be("No model is connected yet.");
        empty.FindAll("button, a").Should().ContainSingle();

        empty.Find("button").Click();

        acted.Should().BeTrue();
    }

    [Fact]
    public void An_empty_state_whose_next_step_is_elsewhere_links_to_it()
    {
        var link = Render<EmptyState>(parameters => parameters
            .Add(component => component.Cause, "A sandbox provider is needed to use this.")
            .Add(component => component.ActionLabel, "Add a sandbox provider")
            .Add(component => component.ActionHref, "/settings/sandbox")).Find("a");

        link.GetAttribute("href").Should().Be("/settings/sandbox");
        link.TextContent.Should().Be("Add a sandbox provider");
    }

    [Theory]
    [InlineData("", "Connect a model")]
    [InlineData("No model is connected yet.", "")]
    public void An_empty_state_without_a_cause_or_an_action_is_refused(string cause, string action)
    {
        var render = () => Render<EmptyState>(parameters => parameters
            .Add(component => component.Cause, cause)
            .Add(component => component.ActionLabel, action)
            .Add(component => component.ActionHref, "/setup"));

        render.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_empty_state_whose_action_does_nothing_is_refused()
    {
        var render = () => Render<EmptyState>(parameters => parameters
            .Add(component => component.Cause, "No model is connected yet.")
            .Add(component => component.ActionLabel, "Connect a model"));

        render.Should().Throw<InvalidOperationException>();
    }

    #endregion
}
