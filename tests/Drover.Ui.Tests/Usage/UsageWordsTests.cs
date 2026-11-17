namespace Drover.Ui.Tests.Usage;

/// <summary>
/// Usage in words and in order: a cost with its currency, an unknown price said in words, what a total leaves out,
/// and the crew as its hierarchy with the biggest spender first. The first two tests came with the starter.
/// </summary>
public sealed class UsageWordsTests
{
    #region Figures

    [Fact]
    public void An_unknown_price_is_said_and_never_shown_as_zero()
    {
        UsageWords.Money(null, "USD").Should().Be("price not known");
        UsageWords.Money(0, "USD").Should().Be("USD 0.00");
        UsageWords.Money(0.004m, "USD").Should().Be("under USD 0.01");
        UsageWords.Money(1234.5m, "USD").Should().Be("USD 1,234.50");
        UsageWords.Money(-1.24m, "USD").Should().Be("a credit of USD 1.24");
        UsageWords.Money(-0.004m, "USD").Should().Be("a credit under USD 0.01");
        UsageWords.LeftOut(new(3.02m, 1, 1, true)).Should().Be("Not counting models whose price is not known");
        UsageWords.LeftOut(new(3.02m, 1, 1)).Should().BeNull();
        UsageWords.LeftOut(new(null, 1, 1, true)).Should().BeNull("a figure with no cost at all already says its price is not known");
        UsageWords.Tokens(120_400).Should().Be("120,400");
    }

    [Fact]
    public void Usage_rows_keep_the_hierarchy_and_put_the_biggest_spender_first_at_each_level()
    {
        UsageNode Bot(string name, decimal? cost, long tokens, params UsageNode[] beneath) =>
            new(Guid.NewGuid(), name, "sample-model", new(cost, tokens, 0), null, beneath);

        var rows = UsageWords.Rows(
        [
            Bot("Cheap", 0.10m, 10),
            Bot("Lead", 5m, 10, Bot("Unpriced small", null, 5), Bot("Helper", 2m, 10), Bot("Unpriced big", null, 500)),
        ]);

        rows.Select(row => (row.Bot.BotName, row.Depth)).Should().Equal(
            ("Lead", 0), ("Helper", 1), ("Unpriced big", 1), ("Unpriced small", 1), ("Cheap", 0));
        UsageWords.IsEmpty([Bot("Idle", null, 0)]).Should().BeTrue();
        UsageWords.IsEmpty([Bot("Busy", null, 1)]).Should().BeFalse();
    }

    #endregion

    #region Sentences

    [Fact]
    public void A_reply_says_what_it_used_and_with_the_bots_it_asked_and_what_that_leaves_out()
    {
        var alone = new RunUsageDetail(Guid.NewGuid(), "USD", new(0.02m, 1_200, 340), null);
        var asked = new RunUsageDetail(Guid.NewGuid(), "USD", new(0.02m, 1_200, 340), new(0.10m, 4_000, 900, true));
        var unpriced = new RunUsageDetail(Guid.NewGuid(), "USD", new(null, 1_200, 340), null);

        UsageWords.Reply(alone).Should().Equal("This reply: USD 0.02, 1,200 tokens in, 340 out.");
        UsageWords.Reply(asked).Should().Equal(
            "This reply: USD 0.02, 1,200 tokens in, 340 out.",
            "With the bots it asked: USD 0.10, 4,000 tokens in, 900 out.",
            "Not counting models whose price is not known.");
        UsageWords.Reply(unpriced).Should().Equal("This reply: price not known, 1,200 tokens in, 340 out.");
    }

    [Fact]
    public void Runs_in_conversations_the_person_cannot_open_are_one_total_that_names_none_of_them()
    {
        UsageWords.Unopened(new(1, new(0.40m, 400, 40)), "USD", afterListed: true)
            .Should().Be("One more run is in a conversation you cannot open: USD 0.40, 400 tokens in, 40 out.");
        UsageWords.Unopened(new(3, new(null, 400, 40, true)), "USD", afterListed: true)
            .Should().Be("3 more runs are in conversations you cannot open. Together: price not known, 400 tokens in, 40 out.");
        UsageWords.Unopened(new(1, new(0.40m, 400, 40)), "USD", afterListed: false)
            .Should().Be("One run is in a conversation you cannot open: USD 0.40, 400 tokens in, 40 out.", "nothing is listed above it to be more than");
        UsageWords.Unopened(new(2, new(0.40m, 400, 40)), "USD", afterListed: false)
            .Should().Be("2 runs are in conversations you cannot open. Together: USD 0.40, 400 tokens in, 40 out.");
    }

    [Fact]
    public void A_call_priced_in_another_currency_is_named_and_never_called_unpriced()
    {
        UsageFigure onlyEuro = new(null, 10, 1, UncountedCurrencies: "EUR");
        UsageFigure mixed = new(1.00m, 10, 1, UncountedCurrencies: "EUR");
        UsageFigure everything = new(1.00m, 10, 1, LeavesOutUnpriced: true, UncountedCurrencies: "EUR, GBP");
        UsageFigure unpricedAndEuro = new(null, 10, 1, LeavesOutUnpriced: true, UncountedCurrencies: "EUR");

        UsageWords.Cost(onlyEuro, "USD").Should().Be("priced in EUR");
        UsageWords.LeftOut(onlyEuro).Should().BeNull("its cost already says where its price is");
        UsageWords.Cost(mixed, "USD").Should().Be("USD 1.00");
        UsageWords.LeftOut(mixed).Should().Be("Not counting calls priced in EUR");
        UsageWords.LeftOut(everything).Should().Be("Not counting models whose price is not known. Not counting calls priced in EUR, GBP");
        UsageWords.Cost(unpricedAndEuro, "USD").Should().Be("price not known");
        UsageWords.LeftOut(unpricedAndEuro).Should().Be("Some calls priced in EUR", "with no sum there is nothing to leave them out of");
        UsageWords.Cost(new(null, 10, 1, LeavesOutUnpriced: true), "USD").Should().Be("price not known");
    }

    [Fact]
    public void Each_period_has_a_name_on_its_control_and_in_the_tables_caption()
    {
        UsagePeriods.All.Select(UsageWords.PeriodName).Should().Equal("Today", "Last 7 days", "Last 30 days");
        UsagePeriods.All.Select(UsageWords.Caption).Should().Equal(
            "What each bot used today", "What each bot used in the last 7 days", "What each bot used in the last 30 days");
        UsageWords.RunsTitle("Lead", UsagePeriods.Week).Should().Be("Lead's most expensive runs in the last 7 days");
    }

    #endregion
}
