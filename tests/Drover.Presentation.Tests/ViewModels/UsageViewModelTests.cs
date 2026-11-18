namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The usage page against a stand-in server: the period control, the crew's table that leads with cost, a chosen
/// bot's most expensive runs, and each empty or failed state. Every name and figure here is a sample.
/// </summary>
public sealed class UsageViewModelTests : ServerScreenTest
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
    public async Task The_crew_is_a_table_for_the_last_7_days_until_another_period_is_chosen()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        var screen = Make<UsageViewModel>();

        await screen.Open(null, null);

        UsageViewModel.Heading.Should().Be("Usage");
        UsageViewModel.PeriodLabel.Should().Be("Period");
        UsageViewModel.Periods.Select(period => period.Label).Should().Equal("Today", "Last 7 days", "Last 30 days");
        screen.Period.Should().Be(UsagePeriods.Week);
        screen.Table!.Caption.Should().Be("What each bot used in the last 7 days");
        screen.Table.Rows.Select(row => row.BotName).Should().Equal("Sample lead", "Sample helper", "Sample unpriced");
        screen.Table.Rows[2].CostOwn.Should().Contain("price not known");
        screen.ShowOwnRunsHint.Should().BeFalse("an owner reads the whole space, so there is nothing to explain");
        screen.ShowRuns.Should().BeFalse("no bot is chosen");
        Server.Addresses.Should().Contain(address => address.StartsWith($"GET {path}?period=7d&zone=", StringComparison.Ordinal));

        screen.Period = UsagePeriods.Today;

        Navigator.Current.Should().Be("/usage?period=today");
    }

    [Fact]
    public async Task The_period_in_the_address_is_the_one_read()
    {
        SignedIn();
        Server.Answer("GET", path, crew with { Period = UsagePeriods.Today });
        var screen = Make<UsageViewModel>();

        await screen.Open("today", null);

        screen.Table!.Caption.Should().Be("What each bot used today");
        screen.Period.Should().Be(UsagePeriods.Today);
        Server.Addresses.Should().Contain(address => address.StartsWith($"GET {path}?period=today&zone=", StringComparison.Ordinal));
        Navigator.Current.Should().Be("/", "opening the screen at an address does not go anywhere");
    }

    [Fact]
    public async Task A_period_that_is_not_one_of_the_three_is_the_last_7_days()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        var screen = Make<UsageViewModel>();

        await screen.Open("sample-made-up-period", null);

        screen.Period.Should().Be(UsagePeriods.Week);
    }

    [Fact]
    public async Task A_period_with_no_runs_says_so_and_nothing_else()
    {
        SignedIn();
        Server.Answer("GET", path, crew with { Crew = [] });
        var screen = Make<UsageViewModel>();

        await screen.Open(null, lead);

        screen.Table!.IsEmpty.Should().BeTrue();
        screen.Table.Rows.Should().BeEmpty();
        UsageTableModel.EmptyNote.Should().Be("No bot ran in this period.");
        screen.ShowRuns.Should().BeFalse("with nothing run there is no bot to choose");
    }

    [Fact]
    public async Task Choosing_a_bot_lists_its_most_expensive_runs_each_linking_to_its_conversation()
    {
        SignedIn();
        var side = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");
        Server.Answer("GET", path, crew);
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD",
        [
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), lead, true, null, DateTimeOffset.UnixEpoch, new(0.70m, 70, 7)),
            new(Guid.CreateVersion7(), side, lead, false, "Sample side chat", DateTimeOffset.UnixEpoch, new(0.30m, 30, 3, true)),
        ], new UnopenedRuns(2, new(0.25m, 25, 2))));
        var screen = Make<UsageViewModel>();

        await screen.Open("7d", lead);

        screen.RunsTitle.Should().Be("Sample lead's most expensive runs in the last 7 days");
        screen.RunLines.Select(line => line.Cost).Should().Equal("USD 0.70", "USD 0.30");
        screen.RunLines.Select(line => line.Address).Should().Equal($"/talk/{lead}", $"/talk/{lead}/side/{side}");
        screen.RunLines[1].Place.Should().EndWith("in Sample side chat");
        screen.RunLines[0].Tokens.Should().Be("70 tokens in, 7 out");
        screen.RunLines.Select(line => line.LeftOut).Should().Equal(null, "Not counting models whose price is not known");
        screen.UnopenedNote.Should().Be("2 more runs are in conversations you cannot open. Together: USD 0.25, 25 tokens in, 2 out.");
        screen.ShowRunsList.Should().BeTrue();
        screen.Table!.Rows.Single(row => row.Chosen).BotName.Should().Be("Sample lead");
    }

    [Fact]
    public async Task While_the_runs_load_the_page_says_what_it_is_finding()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        var holding = Server.Hold("GET", $"{path}/bots/{lead}/runs");
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD", [], null));
        var screen = Make<UsageViewModel>();

        var opened = screen.Open("7d", lead);
        await WaitUntil(() => screen.ShowRunsLoading);

        UsageViewModel.RunsLoadingNote.Should().Be("Finding the runs that cost the most.");
        screen.ShowCrew.Should().BeTrue("the table is there while the runs load");
        holding.SetResult();
        await opened;
        screen.ShowRunsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Choosing_a_bot_moves_focus_to_its_runs_and_changing_the_period_leaves_focus_in_the_control()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD", [], null));
        var screen = Make<UsageViewModel>();
        await screen.Open("7d", null);
        screen.FocusRuns.Should().BeFalse();

        await screen.Open("7d", lead);

        screen.NoRunsNote.Should().Be("Sample lead made no model calls of its own in the last 7 days in a conversation you can open.");
        screen.ShowNoRuns.Should().BeTrue();
        screen.FocusRuns.Should().BeTrue("a bot the person chose takes focus to its runs");

        await screen.Open("30d", lead);

        screen.RunsTitle.Should().EndWith("in the last 30 days");
        screen.FocusRuns.Should().BeFalse("a period chosen with the arrow keys keeps focus in the period control");
    }

    [Fact]
    public async Task A_bot_whose_runs_failed_to_load_does_not_take_focus_when_the_period_changes()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        Server.Answer("GET", $"{path}/bots/{lead}/runs", HttpStatusCode.InternalServerError);
        var screen = Make<UsageViewModel>();
        await screen.Open("7d", null);

        await screen.Open("7d", lead);
        screen.ShowRunsFailed.Should().BeTrue();
        UsageViewModel.RunsFailedTitle.Should().Be("Drover could not list the runs");
        screen.FocusRuns.Should().BeFalse("a failed load leaves nothing to move to");
        Server.Answer("GET", $"{path}/bots/{lead}/runs", new BotRunsUsage(lead, "USD", [], null));
        await screen.Open("30d", lead);

        screen.RunsTitle.Should().EndWith("in the last 30 days");
        screen.ShowRunsFailed.Should().BeFalse();
        screen.FocusRuns.Should().BeFalse("only a bot the person chose moves focus");
    }

    [Fact]
    public async Task Choosing_a_bot_or_a_period_goes_to_the_address_that_names_it_and_says_where_focus_belongs()
    {
        SignedIn();
        Server.Answer("GET", path, crew);
        var screen = Make<UsageViewModel>();
        await screen.Open("30d", null);
        List<UsageFocus> focuses = [];
        screen.Navigating += focuses.Add;

        screen.ChooseBot(helper);
        screen.Period = UsagePeriods.Today;

        Navigator.Current.Should().Be("/usage?period=today");
        focuses.Should().Equal(UsageFocus.Runs, UsageFocus.Period);
    }

    [Fact]
    public async Task A_member_is_told_the_figures_are_their_own_runs()
    {
        SignedInAs("member");
        Server.Answer("GET", path, crew);
        var screen = Make<UsageViewModel>();

        await screen.Open(null, null);

        screen.ShowOwnRunsHint.Should().BeTrue();
        UsageViewModel.OwnRunsHint.Should().Be("Only the runs that acted for you. An owner or admin sees the whole space.");
    }

    [Fact]
    public async Task A_failed_load_says_what_failed_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", path, HttpStatusCode.InternalServerError);
        var screen = Make<UsageViewModel>();

        await screen.Open(null, null);

        screen.ShowFailed.Should().BeTrue();
        screen.ShowCrew.Should().BeFalse();
        UsageViewModel.FailedTitle.Should().Be("Drover could not add up what the bots used");

        Server.Answer("GET", path, crew);
        await screen.LoadCommand.ExecuteAsync(null);
        screen.ShowFailed.Should().BeFalse();
        screen.ShowCrew.Should().BeTrue();
    }

    [Fact]
    public async Task Until_the_server_answers_the_page_says_it_is_adding_up()
    {
        SignedIn();
        var holding = Server.Hold("GET", path);
        Server.Answer("GET", path, crew);
        var screen = Make<UsageViewModel>();

        var opened = screen.Open(null, null);
        await WaitUntil(() => screen.ShowLoading);

        UsageViewModel.LoadingNote.Should().Be("Adding up what each bot used.");
        holding.SetResult();
        await opened;
        screen.ShowLoading.Should().BeFalse();
    }

    #endregion
}
