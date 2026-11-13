namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The status page against a stand-in server: each row says its state in words, the server's rows appear only when
/// the server sends them, and each empty or failed state says what to do. Every name and value here is a sample.
/// </summary>
public sealed class StatusPageTests : ScreenTest
{
    #region Private Fields

    private static readonly string path = $"/api/spaces/{SpaceId}/status";

    #endregion

    #region Public Methods

    [Fact]
    public void Each_row_says_its_state_in_words_under_the_server_or_the_space()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(true,
        [
            new("server", "Database", "ok", "Sample: connected as the application role.", null),
            new("server", "Public address", "warning", "Sample: not set.", null),
            new("space", "Sample model", "problem", "Sample: did not answer.", DateTimeOffset.UnixEpoch),
            new("space", "Sample tools", "off", "Sample: turned off.", null),
        ]));

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.FindAll(".dv-status-group-title").Select(title => title.TextContent).Should().Equal("This server", "This space"));
        page.Find("h1").TextContent.Should().Be("Status");
        page.FindAll("ul[aria-labelledby=dv-status-server] .dv-status-row-name").Select(name => name.TextContent).Should().Equal("Database", "Public address");
        page.FindAll("ul[aria-labelledby=dv-status-space] .dv-status-row-name").Select(name => name.TextContent).Should().Equal("Sample model", "Sample tools");
        page.FindAll(".dv-status-row .dv-badge").Select(badge => badge.TextContent).Should().Equal("Working", "Needs attention", "Not working", "Off");
        page.FindAll(".dv-status-row-time").Should().ContainSingle().Which.TextContent.Should().StartWith("checked ");
        page.Find(".dv-status-header button").TextContent.Trim().Should().Be("Check again");
    }

    [Fact]
    public void Where_the_server_keeps_its_rows_only_the_space_is_shown()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(false, [new("space", "Sample model", "ok", "Sample: answered.", null)]));

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.FindAll(".dv-status-group-title").Select(title => title.TextContent).Should().Equal("This space"));
    }

    [Fact]
    public void A_space_with_no_connection_says_how_to_add_one()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(true, []));

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.Find(".dv-status-group .dv-status-hint").TextContent.Should().Contain("No model or tool connection yet."));
        page.FindAll(".dv-status-hint a").Select(link => link.GetAttribute("href")).Should().Equal("/setup/model", "/tools");
    }

    [Fact]
    public void While_loading_it_says_what_it_is_checking()
    {
        SignedIn();
        Server.Stream(path);

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.Find("p[role=status]").TextContent.Should().Be("Checking the server and the space's connections."));
    }

    [Fact]
    public void A_status_that_fails_to_load_says_so_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", path, HttpStatusCode.ServiceUnavailable);

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the status"));
        page.Find(".dv-empty .dv-button").TextContent.Trim().Should().Be("Try again");
    }

    [Fact]
    public void A_member_who_is_not_an_admin_is_told_who_can_see_it_and_nothing_is_asked()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });

        var page = Render<StatusPage>();

        page.WaitForAssertion(() => page.Find(".dv-status-hint").TextContent.Should().Be("An owner or admin of the space can see its status."));
        Server.Requests.Should().NotContain(request => request.Path == path);
    }

    #endregion
}
