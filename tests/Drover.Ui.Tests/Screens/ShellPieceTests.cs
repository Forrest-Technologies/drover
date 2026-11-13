namespace Drover.Ui.Tests.Screens;

public sealed class ShellPieceTests : ScreenTest
{
    #region Status Note

    [Fact]
    public void A_note_region_is_always_present_and_empty_until_there_is_something_to_say()
    {
        var note = Render<StatusNote>();

        note.Find("[role=status]").ChildElementCount.Should().Be(0);
    }

    [Fact]
    public void A_failure_note_is_an_alert_and_carries_an_icon_as_well_as_color()
    {
        var note = Render<StatusNote>(parameters => parameters
            .Add(component => component.Kind, NoteKind.Danger)
            .Add(component => component.Text, "The key was rejected."));

        var alert = note.Find("[role=alert]");
        alert.TextContent.Trim().Should().Be("The key was rejected.");
        alert.QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    #endregion

    #region Setup Steps

    [Fact]
    public void The_steps_say_where_the_person_is_in_words()
    {
        var steps = Render<SetupSteps>(parameters => parameters.Add(component => component.Current, 2));

        steps.Find("nav").GetAttribute("aria-label").Should().Be("Setup steps");
        steps.Find(".dv-steps-count").TextContent.Should().Be("Step 2 of 3");
        steps.FindAll("li").Select(step => step.TextContent.Trim()).Should().Equal(
            "Create your account, done",
            "Connect a model, this step",
            "Meet your first bot, not started");
        steps.FindAll("li")[1].GetAttribute("aria-current").Should().Be("step");
    }

    #endregion

    #region Account Menu

    [Fact]
    public void The_account_menu_is_named_for_the_person_and_offers_the_themes_the_status_page_and_sign_out()
    {
        Services.GetRequiredService<SessionState>().Begin(Person);

        var menu = Render<AccountMenu>();

        menu.Find("button.dv-menu-trigger").TextContent.Should().Be("Sam Sample");
        menu.WaitForAssertion(() => menu.FindAll("[role=menuitem]").Select(item => item.TextContent.Trim().Split('\n')[0].Trim())
            .Should().Equal("Dark theme", "Light theme", "Match this device", "Status", "Sign out"));
        menu.FindAll("[role=menuitem]")[0].TextContent.Should().Contain(", in use");
    }

    [Fact]
    public void A_member_who_is_not_an_admin_is_not_offered_the_status_page()
    {
        Services.GetRequiredService<SessionState>().Begin(Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });

        var menu = Render<AccountMenu>();

        menu.WaitForAssertion(() => menu.FindAll("[role=menuitem]").Select(item => item.TextContent.Trim().Split('\n')[0].Trim())
            .Should().Equal("Dark theme", "Light theme", "Match this device", "Sign out"));
    }

    [Fact]
    public void Choosing_status_opens_the_status_page()
    {
        Services.GetRequiredService<SessionState>().Begin(Person);
        var menu = Render<AccountMenu>();

        menu.FindAll("[role=menuitem]")[3].Click();

        menu.WaitForAssertion(() => Address.Should().Be("status"));
    }

    [Fact]
    public void Choosing_a_theme_applies_it_and_marks_it_as_in_use()
    {
        Services.GetRequiredService<SessionState>().Begin(Person);
        var menu = Render<AccountMenu>();

        menu.FindAll("[role=menuitem]")[1].Click();

        menu.WaitForAssertion(() => menu.FindAll("[role=menuitem]")[1].TextContent.Should().Contain(", in use"));
        Services.GetRequiredService<ThemeService>().Choice.Should().Be(ThemeChoice.Light);
        JSInterop.Invocations.Should().Contain(call => call.Identifier == "apply" && (string?)call.Arguments[0] == "light");
    }

    [Fact]
    public void Signing_out_ends_the_session_and_opens_sign_in()
    {
        Server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        Services.GetRequiredService<SessionState>().Begin(Person);
        var menu = Render<AccountMenu>();

        menu.FindAll("[role=menuitem]")[4].Click();

        menu.WaitForAssertion(() => Address.Should().Be("sign-in"));
        Services.GetRequiredService<SessionState>().Current.Should().BeNull();
    }

    [Fact]
    public void A_sign_out_that_fails_says_the_person_is_still_signed_in()
    {
        Server.Unreachable = true;
        Services.GetRequiredService<SessionState>().Begin(Person);
        var menu = Render<AccountMenu>();

        menu.FindAll("[role=menuitem]")[4].Click();

        var toasts = Services.GetRequiredService<ToastService>();
        menu.WaitForAssertion(() => toasts.Toasts.Single().Message.Should().StartWith("You are still signed in."));
        Services.GetRequiredService<SessionState>().Current.Should().BeSameAs(Person);
    }

    #endregion
}
