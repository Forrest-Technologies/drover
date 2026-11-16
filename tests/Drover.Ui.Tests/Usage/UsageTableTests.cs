namespace Drover.Ui.Tests.Usage;

/// <summary>
/// The usage table as a component: a real table with a caption and header associations, cost first, the crew as its
/// hierarchy, and an empty period that says so. The first test came with the starter. Every figure is a sample.
/// </summary>
public sealed class UsageTableTests : BunitContext
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    #endregion

    #region Constructors

    public UsageTableTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Public Methods

    [Fact]
    public void Usage_is_a_real_table_that_leads_with_cost_and_an_empty_period_says_so()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.24m, 100, 10), new(3m, 300, 30), [new(helper, "Helper", "sample-model", new(null, 200, 20), null, [])])];
        var table = Render<UsageTable>(set => set
            .Add(view => view.Crew, crew)
            .Add(view => view.Currency, "USD")
            .Add(view => view.Caption, "What each bot used in the last 7 days")
            .Add(view => view.BotHref, id => $"/usage/{id}"));

        table.Find("caption").TextContent.Should().Be("What each bot used in the last 7 days");
        table.FindAll("thead th").Select(header => (header.GetAttribute("scope"), header.TextContent)).Should().Equal(
            ("col", "Bot"), ("col", "Cost"), ("col", "Tokens in"), ("col", "Tokens out"), ("col", "Model"));
        table.FindAll("tbody th[scope=row]").Select(header => header.TextContent.Trim()).Should().Equal("Lead", "Helper");
        table.FindAll("tbody tr")[0].QuerySelector("td")!.TextContent.Should().Contain("USD 1.24").And.Contain("USD 3.00 with the bots beneath it");
        table.FindAll("tbody tr")[1].QuerySelector("td")!.TextContent.Should().Contain("price not known");

        table.Render(set => set.Add(view => view.Crew, []));

        table.Find(".dv-usage-hint").TextContent.Should().Be("No bot ran in this period.");
        table.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_with_bots_beneath_it_labels_both_figures_and_says_once_what_its_total_leaves_out()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10, true), new(2.00m, 300, 30, true), [new(helper, "Helper", "sample-model", new(1.00m, 200, 20), null, [])])];
        var table = Render<UsageTable>(set => set
            .Add(view => view.Crew, crew)
            .Add(view => view.Currency, "USD")
            .Add(view => view.Caption, "What each bot used today")
            .Add(view => view.BotHref, id => $"/usage?bot={id}"));

        var cost = table.FindAll("tbody tr")[0].QuerySelector("td")!;
        cost.QuerySelector(".dv-usage-own")!.TextContent.Should().Be("USD 1.00 on its own");
        cost.TextContent.Should().Contain("USD 2.00 with the bots beneath it");
        cost.QuerySelectorAll(".dv-usage-beneath").Select(line => line.TextContent).Should().ContainSingle(line => line == "Not counting models whose price is not known");
        table.FindAll("tbody tr")[1].QuerySelector(".dv-usage-own")!.TextContent.Should().Be("USD 1.00", "a bot with none beneath it has one figure, its own");
        table.FindAll("tbody th[scope=row] a").Select(link => (link.TextContent, link.GetAttribute("href")))
            .Should().Equal(("Lead", $"/usage?bot={lead}"), ("Helper", $"/usage?bot={helper}"));
        table.FindAll("tbody tr")[0].QuerySelector("td[data-label='Tokens in']")!.TextContent.Should()
            .Contain("100 on its own").And.Contain("300 with the bots beneath it");
        table.FindAll("tbody tr")[1].QuerySelector("td[data-label='Tokens in']")!.TextContent.Trim().Should().Be("200");
        table.FindAll("[aria-current]").Should().BeEmpty();

        table.Render(set => set.Add(view => view.ChosenId, helper));

        table.FindAll("a[aria-current=true]").Should().ContainSingle().Which.TextContent.Should().Be("Helper");
    }

    [Fact]
    public void A_removed_bot_stays_with_its_figures_and_says_it_is_removed()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [], IsRemoved: true), new(helper, "Helper", "sample-model", new(0.50m, 50, 5), null, [])];
        var table = Render<UsageTable>(set => set
            .Add(view => view.Crew, crew)
            .Add(view => view.Currency, "USD")
            .Add(view => view.Caption, "What each bot used today")
            .Add(view => view.BotHref, id => $"/usage?bot={id}"));

        table.FindAll("tbody th[scope=row]").Select(header => header.TextContent.Trim()).Should().Equal("Lead, removed", "Helper");
        table.Find(".dv-usage-removed").TextContent.Should().Be(", removed");
    }

    #endregion
}
