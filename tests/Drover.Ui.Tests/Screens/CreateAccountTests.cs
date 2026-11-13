namespace Drover.Ui.Tests.Screens;

public sealed class CreateAccountTests : ScreenTest
{
    #region Public Methods

    [Fact]
    public void The_step_asks_for_the_account_and_the_setup_code_the_server_printed()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));

        var page = Render<CreateAccount>();

        page.WaitForAssertion(() => page.FindAll("label").Select(label => label.TextContent)
            .Should().Equal("Your name", "Email", "Password", "Setup code"));
        page.Find("h1").TextContent.Should().Be("Create your account");
        page.Find("[aria-current=step]").TextContent.Should().Contain("Create your account");
        page.Find("input[name=new-password]").GetAttribute("autocomplete").Should().Be("new-password");
        page.Find("button[type=submit]").TextContent.Trim().Should().Be("Create account");
    }

    [Fact]
    public void The_setup_code_is_not_asked_for_when_the_server_does_not_need_it()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: false));

        var page = Render<CreateAccount>();

        page.WaitForAssertion(() => page.FindAll("label").Select(label => label.TextContent)
            .Should().Equal("Your name", "Email", "Password"));
    }

    [Fact]
    public void Creating_the_account_signs_in_and_moves_to_the_model_step()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        Server.Answer("POST", "/api/setup", Person, HttpStatusCode.Created);
        var page = Render<CreateAccount>();
        page.WaitForElement("form");

        page.Find("input[name=name]").Input(" Sam Sample ");
        page.Find("input[name=email]").Input("sam@example.org");
        page.Find("input[name=new-password]").Input("a long sample password");
        page.Find("input[name=setup-code]").Input("ABCD-EFGH-JKLM");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Address.Should().Be("setup/model"));
        var sent = JsonSerializer.Deserialize<SetupRequest>(Server.Sent("POST", "/api/setup").Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new SetupRequest("sam@example.org", "Sam Sample", "a long sample password", "ABCD-EFGH-JKLM"));
        Services.GetRequiredService<SessionState>().Current.Should().BeEquivalentTo(Person);
    }

    [Fact]
    public void A_wrong_setup_code_is_reported_with_the_servers_sentence()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        Server.Refuse("POST", "/api/setup", HttpStatusCode.Forbidden, "The setup code is required. It is printed in the server log.");
        Navigation.NavigateTo("/setup");
        var page = Render<CreateAccount>();
        page.WaitForElement("form");

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim()
            .Should().Be("The setup code is required. It is printed in the server log."));
        Address.Should().Be("setup");
    }

    [Fact]
    public void A_server_that_is_already_set_up_hands_over_to_sign_in()
    {
        var page = Render<CreateAccount>();

        page.WaitForAssertion(() => Address.Should().Be("sign-in"));
    }

    [Fact]
    public void A_server_that_cannot_be_reached_offers_one_thing_to_do()
    {
        Server.Unreachable = true;

        var page = Render<CreateAccount>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().Be(Failures.Unreachable));
        page.FindAll("form").Should().BeEmpty();
        page.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Try again");
    }

    #endregion
}
