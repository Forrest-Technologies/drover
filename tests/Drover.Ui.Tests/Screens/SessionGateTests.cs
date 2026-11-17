namespace Drover.Ui.Tests.Screens;

public sealed class SessionGateTests : ScreenTest
{
    #region Public Methods

    [Fact]
    public void A_signed_in_person_sees_what_the_gate_guards()
    {
        SignedIn();

        var gate = RenderGate();

        gate.WaitForAssertion(() => gate.Find("p.guarded").TextContent.Should().Be("Guarded"));
    }

    [Fact]
    public void Nobody_signed_in_on_a_new_server_is_sent_to_setup()
    {
        SignedOut(setupOpen: true);

        var gate = RenderGate();

        gate.WaitForAssertion(() => Address.Should().Be("setup"));
        gate.FindAll("p.guarded").Should().BeEmpty();
    }

    [Fact]
    public void Nobody_signed_in_on_a_set_up_server_is_sent_to_sign_in()
    {
        SignedOut(setupOpen: false);

        var gate = RenderGate();

        gate.WaitForAssertion(() => Address.Should().Be("sign-in"));
    }

    [Fact]
    public void A_server_that_cannot_be_reached_says_so_and_offers_to_try_again()
    {
        Server.Unreachable = true;
        var gate = RenderGate();
        gate.WaitForAssertion(() => gate.Find("h2").TextContent.Should().Be("The server did not answer"));
        gate.Find(".dv-empty-cause").TextContent.Should().Be(Failures.Unreachable);

        Server.Unreachable = false;
        SignedIn();
        gate.Find("button").Click();

        gate.WaitForAssertion(() => gate.Find("p.guarded").TextContent.Should().Be("Guarded"));
    }

    [Fact]
    public void A_native_app_whose_server_does_not_answer_can_be_pointed_at_another()
    {
        var choice = new FakeServerChoice();
        Services.AddSingleton<IServerChoice>(choice);
        Server.Unreachable = true;
        var gate = RenderGate();
        gate.WaitForAssertion(() => gate.Find("h2").TextContent.Should().Be("The server did not answer"));

        gate.Find(".dv-server-choice span").TextContent.Should().Be("This app is connected to drover.example.test.");
        gate.Find(".dv-server-choice button").TextContent.Trim().Should().Be("Use another server");
        gate.Find(".dv-server-choice button").Click();

        choice.Changes.Should().Be(1);
    }

    [Fact]
    public void The_web_app_offers_no_other_server()
    {
        Server.Unreachable = true;

        var gate = RenderGate();

        gate.WaitForAssertion(() => gate.Find("h2").TextContent.Should().Be("The server did not answer"));
        gate.FindAll(".dv-server-choice").Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<SessionGate> RenderGate() =>
        Render<SessionGate>(parameters => parameters.AddChildContent("<p class=\"guarded\">Guarded</p>"));

    #endregion
}
