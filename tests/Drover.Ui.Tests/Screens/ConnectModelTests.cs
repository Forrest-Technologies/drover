namespace Drover.Ui.Tests.Screens;

public sealed class ConnectModelTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly string connections = $"/api/spaces/{SpaceId}/model-connections";

    #endregion

    #region Constructors

    public ConnectModelTests()
    {
        SignedIn();
    }

    #endregion

    #region Public Methods

    [Fact]
    public void The_step_offers_two_providers_and_starts_with_the_key_alone()
    {
        var page = RenderStep();

        page.Find("h1").TextContent.Should().Be("Connect a model");
        page.Find("[aria-current=step]").TextContent.Should().Contain("Connect a model");
        page.Find("fieldset legend").TextContent.Should().Be("Provider");
        page.FindAll("fieldset label").Select(label => label.TextContent.Trim()).Should().Equal("Venice", "Compatible endpoint");
        page.FindAll(".dv-field-label").Select(label => label.TextContent).Should().Equal("Key");
        page.Find("button[type=submit]").TextContent.Trim().Should().Be("Test connection");
    }

    [Fact]
    public void Choosing_a_compatible_endpoint_asks_for_its_base_address()
    {
        var page = RenderStep();

        page.FindAll("input[type=radio]")[1].Change(true);

        page.FindAll(".dv-field-label").Select(label => label.TextContent).Should().Equal("Base address", "Key");
        page.Find("input[type=password]").GetAttribute("autocomplete").Should().Be("off");
    }

    [Fact]
    public void A_passed_test_says_so_in_the_providers_words_and_offers_to_continue()
    {
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(true, "Connected. 42 models are available.", null, Connection("venice", null)));
        var page = RenderStep();

        page.Find("input[type=password]").Input("sample-key");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=status] .dv-note").TextContent.Trim().Should().Be("Connected. 42 models are available."));
        page.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Test again", "Continue");
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Continue").Click();
        Address.Should().Be("setup/bot");
    }

    [Fact]
    public void A_failed_test_says_why_in_an_alert_and_does_not_offer_to_continue()
    {
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(false, "The provider rejected the API key.", "unauthorized", Connection("venice", null)));
        var page = RenderStep();

        page.Find("input[type=password]").Input("wrong-key");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().Be("The provider rejected the API key."));
        page.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Test connection");
    }

    [Fact]
    public void Testing_again_with_a_corrected_key_replaces_the_key_and_makes_no_second_connection()
    {
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("PUT", $"{connections}/{connectionId}/key", Connection("venice", null));
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(false, "The provider rejected the API key.", "unauthorized", Connection("venice", null)));
        var page = RenderStep();
        page.Find("input[type=password]").Input("wrong-key");
        page.Find("form").Submit();
        page.WaitForElement("[role=alert] .dv-note");

        page.Find("input[type=password]").Input("right-key");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Server.Sent("POST", $"{connections}/{connectionId}/test").Should().HaveCount(2));
        Server.Sent("POST", connections).Should().ContainSingle();
        Server.Sent("PUT", $"{connections}/{connectionId}/key").Single().Should().Contain("right-key");
    }

    [Fact]
    public void A_refused_connection_shows_the_servers_sentence()
    {
        Server.Refuse("POST", connections, HttpStatusCode.BadRequest, "Enter the address of the server, starting with http:// or https://.");
        var page = RenderStep();
        page.FindAll("input[type=radio]")[1].Change(true);

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim()
            .Should().Be("Enter the address of the server, starting with http:// or https://."));
    }

    [Fact]
    public void Nobody_signed_in_is_sent_to_sign_in()
    {
        SignedOut(setupOpen: false);

        var page = Render<ConnectModel>();

        page.WaitForAssertion(() => Address.Should().Be("sign-in"));
        page.FindAll("form").Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static ModelConnectionSummary Connection(string kind, string? baseUrl) =>
        new(connectionId, "Sample connection", kind, baseUrl, true, "untested", null, [], null, null);

    private IRenderedComponent<ConnectModel> RenderStep()
    {
        var page = Render<ConnectModel>();
        page.WaitForElement("form");
        return page;
    }

    #endregion
}
