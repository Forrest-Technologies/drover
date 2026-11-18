namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The usage screen keeps period and bot selection in the address and trusts the server's audience filtering.</summary>
public sealed class UsageViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

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

    #endregion

    #region Private Methods

    private UsagePageViewModel Model()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        return new(server.CreateApi(), new(server.CreateApi()), navigator, NullLogger<UsagePageViewModel>.Instance);
    }

    #endregion
}
