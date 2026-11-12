namespace Drover.Ui.Tests.Screens;

public sealed class SignInTests : ScreenTest
{
    #region Public Methods

    [Fact]
    public void The_screen_is_a_form_with_two_labeled_fields_and_one_named_action()
    {
        SignedOut(setupOpen: false);

        var page = Render<SignIn>();

        page.Find("h1").TextContent.Should().Be("Sign in");
        page.Find("form").Should().NotBeNull();
        LabelOf(page, "input[type=email]").Should().Be("Email");
        LabelOf(page, "input[type=password]").Should().Be("Password");
        page.Find("input[type=email]").GetAttribute("autocomplete").Should().Be("username");
        page.Find("input[type=password]").GetAttribute("autocomplete").Should().Be("current-password");
        var action = page.Find("button[type=submit]");
        action.TextContent.Trim().Should().Be("Sign in");
        action.ClassList.Should().Contain("dv-button-primary");
    }

    [Fact]
    public void Signing_in_opens_the_workspace()
    {
        SignedOut(setupOpen: false);
        Server.Answer("POST", "/api/sign-in", Person);
        var page = Render<SignIn>();

        page.Find("input[type=email]").Input("sam@example.org");
        page.Find("input[type=password]").Input("a long sample password");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Address.Should().BeEmpty());
        Server.Sent("POST", "/api/sign-in").Single().Should().Contain("sam@example.org");
        Services.GetRequiredService<SessionState>().Current.Should().BeEquivalentTo(Person);
    }

    [Fact]
    public void A_refused_sign_in_says_why_in_an_alert_and_leaves_the_form_usable()
    {
        SignedOut(setupOpen: false);
        Server.Refuse("POST", "/api/sign-in", HttpStatusCode.Unauthorized, "The email or password is not right.");
        Navigation.NavigateTo("/sign-in");
        var page = Render<SignIn>();

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().Be("The email or password is not right."));
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeFalse();
        Address.Should().Be("sign-in");
    }

    [Fact]
    public void A_server_that_cannot_be_reached_is_said_in_words()
    {
        Server.Unreachable = true;

        var page = Render<SignIn>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().Be(Failures.Unreachable));
    }

    [Fact]
    public void A_new_server_sends_the_person_to_setup()
    {
        SignedOut(setupOpen: true);

        var page = Render<SignIn>();

        page.WaitForAssertion(() => Address.Should().Be("setup"));
    }

    [Fact]
    public void Someone_already_signed_in_goes_straight_to_the_workspace()
    {
        SignedIn();
        Navigation.NavigateTo("/sign-in");

        var page = Render<SignIn>();

        page.WaitForAssertion(() => Address.Should().BeEmpty());
    }

    #endregion

    #region Private Methods

    private static string LabelOf(IRenderedComponent<SignIn> page, string selector) =>
        page.Find($"label[for={page.Find(selector).Id}]").TextContent;

    #endregion
}
