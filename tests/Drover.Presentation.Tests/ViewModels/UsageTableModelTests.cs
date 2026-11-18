namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The usage table: a table with a caption and named columns, cost first, the crew as its hierarchy, and an empty
/// period that says so. Every figure is a sample.
/// </summary>
public sealed class UsageTableModelTests
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    #endregion

    #region Public Methods

    [Fact]
    public void Usage_is_a_real_table_that_leads_with_cost_and_an_empty_period_says_so()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.24m, 100, 10), new(3m, 300, 30), [new(helper, "Helper", "sample-model", new(null, 200, 20), null, [])])];

        var table = new UsageTableModel(crew, "USD", "What each bot used in the last 7 days", id => $"/usage/{id}");

        table.Caption.Should().Be("What each bot used in the last 7 days");
        UsageTableModel.Columns.Should().Equal("Bot", "Cost", "Tokens in", "Tokens out", "Model");
        table.Rows.Select(row => row.Label).Should().Equal("Lead", "Helper");
        table.Rows[0].CostOwn.Should().Contain("USD 1.24");
        table.Rows[0].CostBeneath.Should().Be("USD 3.00 with the bots beneath it");
        table.Rows[1].CostOwn.Should().Contain("price not known");
        table.Rows.Select(row => row.Depth).Should().Equal(0, 1);

        var empty = new UsageTableModel([], "USD", "What each bot used in the last 7 days", id => $"/usage/{id}");

        UsageTableModel.EmptyNote.Should().Be("No bot ran in this period.");
        empty.IsEmpty.Should().BeTrue();
        empty.Rows.Should().BeEmpty();
    }

    [Fact]
    public void A_bot_with_bots_beneath_it_labels_both_figures_and_says_once_what_its_total_leaves_out()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10, true), new(2.00m, 300, 30, true), [new(helper, "Helper", "sample-model", new(1.00m, 200, 20), null, [])])];

        var table = new UsageTableModel(crew, "USD", "What each bot used today", id => $"/usage?bot={id}");

        var top = table.Rows[0];
        top.CostOwn.Should().Be("USD 1.00 on its own");
        top.CostBeneath.Should().Be("USD 2.00 with the bots beneath it");
        top.LeftOut.Should().Be("Not counting models whose price is not known", "it is said once, for the total that holds the bot's own");
        table.Rows[1].CostOwn.Should().Be("USD 1.00", "a bot with none beneath it has one figure, its own");
        table.Rows[1].CostBeneath.Should().BeNull();
        table.Rows.Select(row => (row.BotName, row.Address)).Should().Equal(("Lead", $"/usage?bot={lead}"), ("Helper", $"/usage?bot={helper}"));
        top.TokensInOwnWords.Should().Be("100 on its own");
        top.TokensInBeneathWords.Should().Be("300 with the bots beneath it");
        top.TokensOutOwnWords.Should().Be("10 on its own");
        top.TokensOutBeneathWords.Should().Be("30 with the bots beneath it");
        table.Rows[1].TokensInOwnWords.Should().Be("200");
        table.Rows[1].TokensInBeneathWords.Should().BeNull();
        table.Rows.Should().OnlyContain(row => !row.Chosen);

        var chosen = new UsageTableModel(crew, "USD", "What each bot used today", id => $"/usage?bot={id}", helper);

        chosen.Rows.Single(row => row.Chosen).BotName.Should().Be("Helper");
    }

    [Fact]
    public void A_removed_bot_stays_with_its_figures_and_says_it_is_removed()
    {
        UsageNode[] crew = [new(lead, "Lead", "sample-model", new(1.00m, 100, 10), null, [], IsRemoved: true), new(helper, "Helper", "sample-model", new(0.50m, 50, 5), null, [])];

        var table = new UsageTableModel(crew, "USD", "What each bot used today", id => $"/usage?bot={id}");

        table.Rows.Select(row => row.Label).Should().Equal("Lead, removed", "Helper");
        table.Rows[0].Removed.Should().BeTrue();
        table.Rows[0].BotName.Should().Be("Lead");
    }

    [Fact]
    public void The_biggest_spender_comes_first_at_every_level_and_each_bot_keeps_its_look()
    {
        UsageNode[] crew =
        [
            new(helper, "Small", "sample-model", new(0.10m, 10, 1), null, []),
            new(lead, "Big", "sample-model", new(9.00m, 900, 90), null, []),
        ];

        var table = new UsageTableModel(crew, "USD", "What each bot used today", id => $"/usage?bot={id}");

        table.Rows.Select(row => row.BotName).Should().Equal("Big", "Small");
        table.Rows[0].Hue.Should().Be(BotIdentity.Hue(lead));
    }

    #endregion
}
