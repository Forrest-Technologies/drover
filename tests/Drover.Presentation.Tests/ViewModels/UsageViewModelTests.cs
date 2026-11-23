namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The usage screen keeps period and bot selection in the address and trusts the server's audience filtering. Its
/// crew is a table with a caption and named columns, cost first, the crew as its hierarchy, and an empty period that
/// says so: the cases of the web client's table and page tests, asked of the view model. Every figure is a sample.
/// </summary>
public sealed class UsageViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public void An_unknown_period_uses_the_week_and_preserves_the_chosen_bot_when_changing_periods()
    {
        var model = Model();
        model.PeriodInAddress = "sample-unknown";
        model.Bot = botId;

        model.Period.Should().Be("7d");
        model.ChoosePeriod("today");
        navigator.Current.Should().Be(Addresses.UsageFor("today", botId));
    }

    [Fact]
    public async Task Only_a_bot_in_the_visible_crew_gets_a_runs_request()
    {
        server.Answer("GET", $"{space}/usage", new Drover.Contracts.Runs.CrewUsage("7d", DateTimeOffset.UnixEpoch, "usd", []));
        var model = Model();
        model.Bot = botId;

        await model.Open();

        model.ChosenBot.Should().BeNull();
        server.Requests.Should().NotContain(request => request.Path.Contains("/runs", StringComparison.Ordinal));
        model.Crew.Should().NotBeNull();
    }

    [Fact]
    public async Task A_selected_bot_loads_its_costliest_runs_without_requesting_another_bots_runs()
    {
        var node = new Drover.Contracts.Runs.UsageNode(botId, "Sample bot", "sample-model", new(1m, 30, 20), null, []);
        server.Answer("GET", $"{space}/usage", new Drover.Contracts.Runs.CrewUsage("7d", DateTimeOffset.UnixEpoch, "usd", [node]));
        server.Answer("GET", $"{space}/usage/bots/{botId}/runs", new Drover.Contracts.Runs.BotRunsUsage(botId, "usd", [], new(2, new(1m, 10, 20))));
        var model = Model();
        model.Bot = botId;

        await model.Open();

        model.ChosenBot.Should().BeEquivalentTo(node);
        model.Runs!.Unopened!.Count.Should().Be(2);
        server.Requests.Where(request => request.Path.EndsWith("/runs", StringComparison.Ordinal)).Should().ContainSingle();
    }

    [Fact]
    public async Task Usage_is_a_real_table_that_leads_with_cost_and_an_empty_period_says_so()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.24m, 100, 10), new(3m, 300, 30), [new(helper, "Helper", "sample-model", new(null, 200, 20), null, [])])];
        var model = await Opened(crew);

        model.Caption.Should().Be("What each bot used in the last 7 days");
        UsagePageViewModel.Columns.Should().Equal("Bot", "Cost", "Tokens in", "Tokens out", "Model");
        model.NothingRan.Should().BeFalse();
        model.Rows.Select(row => row.Label).Should().Equal("Lead", "Helper");
        model.Rows[0].CostOwn.Should().Contain("USD 1.24");
        model.Rows[0].CostBeneath.Should().Be("USD 3.00 with the bots beneath it");
        model.Rows[1].CostOwn.Should().Contain("price not known");
        model.Rows.Select(row => row.Depth).Should().Equal(0, 1);

        var empty = await Opened([]);

        UsagePageViewModel.NoBotRan.Should().Be("No bot ran in this period.");
        empty.NothingRan.Should().BeTrue();
        empty.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task The_caption_names_the_period_in_the_address()
    {
        var model = await Opened([new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [])], period: "today");

        model.Caption.Should().Be("What each bot used today");
        server.Addresses.Should().Contain(address => address.StartsWith($"GET {space}/usage?period=today&zone=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bot_with_bots_beneath_it_labels_both_figures_and_says_once_what_its_total_leaves_out()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10, true), new(2.00m, 300, 30, true), [new(helper, "Helper", "sample-model", new(1.00m, 200, 20), null, [])])];
        var model = await Opened(crew, period: "today");

        var top = model.Rows[0];
        top.CostOwn.Should().Be("USD 1.00 on its own");
        top.CostBeneath.Should().Be("USD 2.00 with the bots beneath it");
        top.LeftOut.Should().Be("Not counting models whose price is not known", "it is said once, for the total that holds the bot's own");
        model.Rows[1].CostOwn.Should().Be("USD 1.00", "a bot with none beneath it has one figure, its own");
        model.Rows[1].CostBeneath.Should().BeNull();
        model.Rows.Select(row => (row.BotName, row.Address)).Should().Equal(
            ("Lead", Addresses.UsageFor("today", lead)), ("Helper", Addresses.UsageFor("today", helper)));
        top.TokensInOwnWords.Should().Be("100 on its own");
        top.TokensInBeneathWords.Should().Be("300 with the bots beneath it");
        top.TokensOutOwnWords.Should().Be("10 on its own");
        top.TokensOutBeneathWords.Should().Be("30 with the bots beneath it");
        model.Rows[1].TokensInOwnWords.Should().Be("200");
        model.Rows[1].TokensInBeneathWords.Should().BeNull();
        model.Rows.Should().OnlyContain(row => !row.Chosen);

        var chosen = await Opened(crew, period: "today", bot: helper);

        chosen.Rows.Should().ContainSingle(row => row.Chosen).Which.BotName.Should().Be("Helper");
    }

    [Fact]
    public async Task A_removed_bot_stays_with_its_figures_and_says_it_is_removed()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [], IsRemoved: true), new(helper, "Helper", "sample-model", new(0.50m, 50, 5), null, [])];
        var model = await Opened(crew);

        model.Rows.Select(row => row.Label).Should().Equal("Lead, removed", "Helper");
        model.Rows[0].Removed.Should().BeTrue();
        model.Rows[0].BotName.Should().Be("Lead");
    }

    [Fact]
    public async Task The_biggest_spender_comes_first_at_every_level_and_each_bot_keeps_its_look()
    {
        UsageNode[] crew =
        [
            new(helper, "Small", "sample-model", new(0.10m, 10, 1), null, []),
            new(lead, "Big", "sample-model", new(9.00m, 900, 90), null, []),
        ];
        var model = await Opened(crew);

        model.Rows.Select(row => row.BotName).Should().Equal("Big", "Small");
        model.Rows[0].Hue.Should().Be(BotIdentity.Hue(lead));
    }

    [Fact]
    public async Task Choosing_a_bot_moves_the_focus_to_its_runs_once_and_a_period_or_a_first_load_does_not()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [])];
        server.Answer("GET", $"{space}/usage/bots/{lead}/runs", new BotRunsUsage(lead, "USD", [], null));
        var model = await Opened(crew, bot: lead);

        model.TakeRunsFocus().Should().BeFalse("a page opened at a bot's address keeps the focus where the page put it");

        model.Bot = null;
        await model.Open();
        model.Bot = lead;
        await model.Open();

        model.TakeRunsFocus().Should().BeTrue("the person just chose this bot");
        model.TakeRunsFocus().Should().BeFalse("the focus moves once, not on every redraw");

        model.PeriodInAddress = "today";
        server.Answer("GET", $"{space}/usage", new CrewUsage("today", DateTimeOffset.UnixEpoch, "USD", crew));
        await model.Open();

        model.TakeRunsFocus().Should().BeFalse("a period change keeps the focus in the period control");
    }

    [Fact]
    public async Task Runs_that_could_not_be_listed_leave_the_focus_where_it_is()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [])];
        var model = await Opened(crew);
        server.Answer("GET", $"{space}/usage/bots/{lead}/runs", HttpStatusCode.InternalServerError);

        model.Bot = lead;
        await model.Open();

        model.RunsProblem.Should().NotBeNull();
        model.TakeRunsFocus().Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private async Task<UsagePageViewModel> Opened(IReadOnlyList<UsageNode> crew, string period = "7d", Guid? bot = null)
    {
        server.Answer("GET", $"{space}/usage", new CrewUsage(period, DateTimeOffset.UnixEpoch, "USD", crew));
        server.Answer("GET", $"{space}/usage/bots/{bot}/runs", new BotRunsUsage(bot ?? Guid.Empty, "USD", [], null));
        var model = Model();
        model.PeriodInAddress = period;
        model.Bot = bot;
        await model.Open();
        return model;
    }

    private UsagePageViewModel Model()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        return new(server.CreateApi(), new(server.CreateApi(), new ShareInbox()), navigator, NullLogger<UsagePageViewModel>.Instance);
    }

    #endregion
}
