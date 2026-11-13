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

    #endregion

    #region Private Methods

    private IRenderedComponent<SessionGate> RenderGate() =>
        Render<SessionGate>(parameters => parameters.AddChildContent("<p class=\"guarded\">Guarded</p>"));

    #endregion
}
