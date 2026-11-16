namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The usage page against a stand-in server: the period control, the crew's table that leads with cost, a chosen
/// bot's most expensive runs, and each empty or failed state. Every name and figure here is a sample.
/// </summary>
public sealed class UsagePageTests : ScreenTest
{
    #region Private Fields

    private static readonly string path = $"/api/spaces/{SpaceId}/usage";

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly CrewUsage crew = new(UsagePeriods.Week, DateTimeOffset.UnixEpoch, "USD",
    [
        new(lead, "Sample lead", "sample-model", new(1.00m, 100, 10), new(1.50m, 300, 30, true),
        [
            new(helper, "Sample helper", "sample-model", new(0.50m, 150, 15), null, []),
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a3"), "Sample unpriced", "sample-unpriced", new(null, 50, 5), null, []),
        ]),
    ]);

    #endregion

    #region Public Methods

    [Fact]
    public void The_crew_is_a_table_for_the_last_7_days_until_another_period_is_chosen()
    {
        SignedIn();
        Server.Answer("GET", path, crew);

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find("caption").TextContent.Should().Be("What each bot used in the last 7 days"));
        page.Find("h1").TextContent.Should().Be("Usage");
        page.Find("legend").TextContent.Should().Be("Period");
        page.FindAll(".dv-segment").Select(segment => segment.TextContent.Trim()).Should().Equal("Today", "Last 7 days", "Last 30 days");
        page.FindAll(".dv-segment-input")[1].HasAttribute("checked").Should().BeTrue();
        page.FindAll("tbody th[scope=row]").Select(row => row.TextContent.Trim()).Should().Equal("Sample lead", "Sample helper", "Sample unpriced");
        page.FindAll("tbody tr")[2].QuerySelector("td")!.TextContent.Should().Contain("price not known");
        page.FindAll(".dv-usage-page-hint").Should().BeEmpty("an owner reads the whole space, so there is nothing to explain");
        page.FindAll("canvas, figure").Should().BeEmpty("the page has no chart");
        Server.Addresses.Should().Contain(address => address.StartsWith($"GET {path}?period=7d&zone=", StringComparison.Ordinal));

        page.FindAll(".dv-segment-input")[0].Change(true);

        Address.Should().Be("usage?period=today");
    }

    [Fact]
    public void The_period_in_the_address_is_the_one_read()
    {
        SignedIn();
        Server.Answer("GET", path, crew with { Period = UsagePeriods.Today });
        Navigation.NavigateTo("/usage?period=today");

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find("caption").TextContent.Should().Be("What each bot used today"));
        Server.Addresses.Should().Contain(address => address.StartsWith($"GET {path}?period=today&zone=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_period_with_no_runs_says_so_and_nothing_else()
    {
        SignedIn();
        Server.Answer("GET", path, crew with { Crew = [] });

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find(".dv-usage-hint").TextContent.Should().Be("No bot ran in this period."));
        page.FindAll("table").Should().BeEmpty();
        page.FindAll("section").Should().BeEmpty();
    }

    [Fact]
    public void Choosing_a_bot_lists_its_most_expensive_runs_each_linking_to_its_conversation()
    {
        SignedIn();
        var side = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");
        Server.Answer("GET", path, crew);
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD",
        [
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), lead, true, null, DateTimeOffset.UnixEpoch, new(0.70m, 70, 7)),
            new(Guid.CreateVersion7(), side, lead, false, "Sample side chat", DateTimeOffset.UnixEpoch, new(0.30m, 30, 3, true)),
        ], new UnopenedRuns(2, new(0.25m, 25, 2))));
        Navigation.NavigateTo($"/usage?period=7d&bot={lead}");

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find("#dv-usage-runs-title").TextContent.Should().Be("Sample lead's most expensive runs in the last 7 days"));
        page.Find("section").GetAttribute("aria-labelledby").Should().Be("dv-usage-runs-title");
        page.FindAll(".dv-usage-run-cost").Select(cost => cost.TextContent).Should().Equal("USD 0.70", "USD 0.30");
        page.FindAll(".dv-usage-run a").Select(link => link.GetAttribute("href")).Should().Equal($"/talk/{lead}", $"/talk/{lead}/side/{side}");
        page.FindAll(".dv-usage-run a")[1].TextContent.Should().EndWith("in Sample side chat");
        page.Find(".dv-usage-run-note").TextContent.Should().Be("Not counting models whose price is not known");
        page.Find(".dv-usage-runs .dv-usage-page-hint").TextContent.Should().Be(
            "2 more runs are in conversations you cannot open. Together: USD 0.25, 25 tokens in, 2 out.");
    }

    [Fact]
    public void Choosing_a_bot_moves_focus_to_its_runs_and_changing_the_period_leaves_focus_in_the_control()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD", [], null));
        var page = Render<UsagePage>();
        page.WaitForAssertion(() => page.Find("caption"));

        Navigation.NavigateTo($"/usage?period=7d&bot={lead}");

        page.WaitForAssertion(() => page.Find(".dv-usage-runs .dv-usage-page-hint").TextContent.Should().Be(
            "Sample lead made no model calls of its own in the last 7 days in a conversation you can open."));
        page.WaitForAssertion(() => Focuses().Should().Be(1));
        page.Find("a[aria-current=true]").TextContent.Should().Be("Sample lead");

        page.FindAll(".dv-segment-input")[2].Change(true);

        page.WaitForAssertion(() => page.Find("#dv-usage-runs-title").TextContent.Should().EndWith("in the last 30 days"));
        Focuses().Should().Be(1, "a period chosen with the arrow keys keeps focus in the period control");
    }

    [Fact]
    public void A_member_is_told_the_figures_are_their_own_runs()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = "member" }] });
        Server.Answer("GET", path, crew);

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find(".dv-usage-page-hint").TextContent.Should().Be(
            "Only the runs that acted for you. An owner or admin sees the whole space."));
    }

    [Fact]
    public void A_failed_load_says_what_failed_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", path, HttpStatusCode.InternalServerError);

        var page = Render<UsagePage>();

        page.WaitForAssertion(() => page.Find(".dv-empty").Should().NotBeNull());
        page.Find(".dv-empty button").TextContent.Trim().Should().Be("Try again");
    }

    #endregion

    #region Private Methods

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal));

    #endregion
}
