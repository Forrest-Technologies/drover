namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The status page against a stand-in server: each row says its state in words, the server's rows appear only when
/// the server sends them, and each empty or failed state says what to do. Every name and value here is a sample.
/// </summary>
public sealed class StatusViewModelTests : ServerScreenTest
{
    #region Private Fields

    private static readonly string path = $"/api/spaces/{SpaceId}/status";

    #endregion

    #region Public Methods

    [Fact]
    public async Task Each_row_says_its_state_in_words_under_the_server_or_the_space()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(true,
        [
            new("server", "Database", "ok", "Sample: connected as the application role.", null),
            new("server", "Public address", "warning", "Sample: not set.", null),
            new("space", "Sample model", "problem", "Sample: did not answer.", DateTimeOffset.UnixEpoch),
            new("space", "Sample tools", "off", "Sample: turned off.", null),
        ]));
        var screen = Make<StatusViewModel>();

        await screen.Start();

        screen.ShowReport.Should().BeTrue();
        StatusViewModel.Heading.Should().Be("Status");
        StatusViewModel.ServerHeading.Should().Be("This server");
        StatusViewModel.SpaceHeading.Should().Be("This space");
        screen.ServerRows.Select(row => row.Name).Should().Equal("Database", "Public address");
        screen.SpaceRows.Select(row => row.Name).Should().Equal("Sample model", "Sample tools");
        screen.ServerRows.Concat(screen.SpaceRows).Select(row => row.Word).Should().Equal("Working", "Needs attention", "Not working", "Off");
        screen.ServerRows.Concat(screen.SpaceRows).Select(row => row.Kind).Should().Equal(BadgeKind.Ok, BadgeKind.Warn, BadgeKind.Danger, BadgeKind.Neutral);
        screen.ServerRows.Concat(screen.SpaceRows).Select(row => row.Summary).Should().Equal(
            "Sample: connected as the application role.", "Sample: not set.", "Sample: did not answer.", "Sample: turned off.");
        screen.ServerRows.Concat(screen.SpaceRows).Where(row => row.Checked is not null).Should().ContainSingle()
            .Which.Checked.Should().StartWith("checked ");
        screen.ShowCheckAgain.Should().BeTrue();
        screen.CanCheckAgain.Should().BeTrue();
    }

    [Fact]
    public async Task Where_the_server_keeps_its_rows_only_the_space_is_shown()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(false, [new("space", "Sample model", "ok", "Sample: answered.", null)]));
        var screen = Make<StatusViewModel>();

        await screen.Start();

        screen.ShowServer.Should().BeFalse();
        screen.ShowSpaceRows.Should().BeTrue();
    }

    [Fact]
    public async Task A_space_with_no_connection_says_how_to_add_one()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(true, []));
        var screen = Make<StatusViewModel>();

        await screen.Start();

        screen.ShowNoSpaceRows.Should().BeTrue();
        screen.ShowSpaceRows.Should().BeFalse();
        StatusViewModel.NoSpaceRowsBefore.Should().Be("No model or tool connection yet.");
        StatusViewModel.AddModelLabel.Should().Be("Add a model connection");
        StatusViewModel.AddToolLabel.Should().Be("a tool server");
        StatusViewModel.NoSpaceRowsAfter.Should().Be("and it is listed here.");
    }

    [Fact]
    public async Task While_loading_it_says_what_it_is_checking()
    {
        SignedIn();
        var loading = Server.Hold("GET", path);
        Server.Answer("GET", path, new StatusReport(true, []));
        var screen = Make<StatusViewModel>();

        screen.ShowChecking.Should().BeTrue();
        var started = screen.Start();
        await WaitUntil(() => screen.Loading);

        screen.ShowChecking.Should().BeTrue();
        screen.ShowCheckAgain.Should().BeFalse("there is nothing to check again before the first report");
        StatusViewModel.CheckingNote.Should().Be("Checking the server and the space's connections.");
        loading.SetResult();
        await started;
        screen.ShowChecking.Should().BeFalse();
    }

    [Fact]
    public async Task A_status_that_fails_to_load_says_so_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", path, HttpStatusCode.ServiceUnavailable);
        var screen = Make<StatusViewModel>();

        await screen.Start();

        screen.ShowFailed.Should().BeTrue();
        screen.LoadProblem.Should().NotBeNullOrEmpty();
        StatusViewModel.FailedTitle.Should().Be("Drover could not load the status");

        Server.Answer("GET", path, new StatusReport(true, []));
        await screen.LoadCommand.ExecuteAsync(null);
        screen.ShowFailed.Should().BeFalse();
        screen.ShowReport.Should().BeTrue();
    }

    [Fact]
    public async Task A_member_who_is_not_an_admin_is_told_who_can_see_it_and_nothing_is_asked()
    {
        SignedInAs("member");
        var screen = Make<StatusViewModel>();

        await screen.Start();

        screen.ShowRestricted.Should().BeTrue();
        StatusViewModel.RestrictedNote.Should().Be("An owner or admin of the space can see its status.");
        screen.ShowChecking.Should().BeFalse();
        Server.Requests.Should().NotContain(request => request.Path == path);
    }

    [Fact]
    public async Task Checking_again_keeps_the_report_in_view_while_it_asks()
    {
        SignedIn();
        Server.Answer("GET", path, new StatusReport(true, [new("space", "Sample model", "ok", "Sample: answered.", null)]));
        var screen = Make<StatusViewModel>();
        await screen.Start();
        var again = Server.Hold("GET", path);

        var checking = screen.LoadCommand.ExecuteAsync(null);
        await WaitUntil(() => screen.Loading);

        screen.ShowReport.Should().BeTrue();
        screen.CanCheckAgain.Should().BeFalse("the button is off while the server is being asked");
        again.SetResult();
        await checking;
        screen.CanCheckAgain.Should().BeTrue();
    }

    #endregion
}
