namespace Drover.Ui.Tests.Starters;

/// <summary>
/// The wording and layout logic the starters carry. When a starter moves into the product, its tests move with
/// it and stay green: the sentences here are the design.
/// </summary>
public sealed class StarterWordsTests
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly Guid scout = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a3");

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region History

    [Fact]
    public void A_text_is_compared_line_by_line_with_removals_before_additions()
    {
        var lines = LineDiff.Between("one\ntwo\nthree", "one\ntwo, changed\nthree\nfour");

        lines.Should().Equal(
            new DiffLine(DiffKind.Same, "one"),
            new DiffLine(DiffKind.Removed, "two"),
            new DiffLine(DiffKind.Added, "two, changed"),
            new DiffLine(DiffKind.Same, "three"),
            new DiffLine(DiffKind.Added, "four"));
        LineDiff.Summary(lines).Should().Be("2 lines added, 1 removed");
        LineDiff.Between(null, "new").Should().Equal(new DiffLine(DiffKind.Added, "new"));
        LineDiff.Between("same", "same").Should().OnlyContain(line => line.Kind == DiffKind.Same);
        LineDiff.Summary(LineDiff.Between("same", "same")).Should().Be("No lines changed");
        LineDiff.Summary(LineDiff.Between("a\n", "a")).Should().Be("No lines changed", "a final line ending is not a line");
        LineDiff.Summary(LineDiff.Between("a\n\n\n", "a")).Should().Be("2 lines removed", "blank lines before the final ending are lines");
    }

    [Fact]
    public void A_history_entry_is_a_sentence_and_an_unknown_action_is_never_guessed_at()
    {
        HistoryEntry Entry(HistoryActor actor, string action, params HistoryChange[] changes) =>
            new(Guid.NewGuid(), actor, "Sample lead", "Sample person", action, start, changes, false);

        HistoryWords.Sentence(Entry(HistoryActor.You, "bot.updated", new("instructions", "a", "b"), new("name", "a", "b")))
            .Should().Be("You changed the instructions and the name");
        HistoryWords.Sentence(Entry(HistoryActor.Bot, "bot.paused")).Should().Be("Sample lead, for Sample person, paused the bot");
        HistoryWords.Sentence(Entry(HistoryActor.Member, "bot.created")).Should().Be("Sample lead made this bot");
        HistoryWords.Sentence(Entry(HistoryActor.You, "thing.unheard_of")).Should().Be("You made a change (thing.unheard_of)");
        HistoryWords.Heading("instructions").Should().Be("The instructions");
        HistoryWords.IsLong(new("instructions", "one\ntwo", "one")).Should().BeTrue();
        HistoryWords.IsLong(new("name", "Sample", "Sample two")).Should().BeFalse();
    }

    #endregion

    #region Usage

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

    #region Timeline

    [Fact]
    public void A_run_is_laid_out_with_a_lane_for_each_bot_in_the_order_they_joined()
    {
        var model = Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], Run());

        model.Lanes.Select(lane => lane.Name).Should().Equal("Lead", "Helper");
        model.Nodes.Select(node => (node.Event.Label, node.Lane, node.Row, node.BranchLane)).Should().Equal(
            ("Thought", 0, 0, null),
            ("Handed work", 0, 1, 1),
            ("Ran a command", 1, 2, null),
            ("Answered", 0, 3, null));
        Timelines.Sentence(model, model.Nodes[1]).Should().Be("Lead: Handed work to Helper. Done, took 30 seconds.");
        Timelines.Sentence(model, model.Nodes[2]).Should().Be("Helper: Ran a command. Working.");
    }

    [Fact]
    public void A_bot_that_was_removed_still_has_a_lane_and_events_given_out_of_order_are_put_in_time_order()
    {
        var model = Timelines.Build([], Run().Reverse());

        model.Lanes.Should().OnlyContain(lane => lane.Name == "A bot that was removed");
        model.Nodes.Select(node => node.Event.Label).Should().Equal("Thought", "Handed work", "Ran a command", "Answered");
    }

    [Theory]
    [InlineData(1, "ArrowDown", 2)]
    [InlineData(1, "ArrowUp", 0)]
    [InlineData(0, "ArrowUp", 0)]
    [InlineData(3, "ArrowDown", 3)]
    [InlineData(1, "ArrowRight", 2)]
    [InlineData(2, "ArrowLeft", 1)]
    [InlineData(2, "ArrowRight", 2)]
    [InlineData(2, "Home", 0)]
    [InlineData(0, "End", 3)]
    [InlineData(1, "a", 1)]
    public void Arrow_keys_move_through_time_and_across_lanes(int from, string key, int to)
    {
        var model = Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], Run());

        Timelines.Move(model, from, key).Should().Be(to);
    }

    #endregion

    #region Crew Tree

    [Fact]
    public void A_bot_sits_centered_over_the_bots_beneath_it()
    {
        var plan = TreeLayout.Plan(Crew());

        plan.Columns.Should().Be(6);
        plan.Rows.Should().Be(3);
        plan.Places.Select(place => (place.Bot.Name, place.Column, place.Depth)).Should().Equal(
            ("Lead", 2, 0), ("Helper", 0, 1), ("Under helper", 0, 2), ("Scout", 2, 1), ("Writer", 4, 1));
        plan.Places.Single(place => place.Bot.Name == "Scout").Should().Match<TreePlace>(place => place.Level == 2 && place.Position == 2 && place.Siblings == 3);
        plan.Edges.Should().HaveCount(4);
        plan.Edges.Single(edge => edge.ChildId == scout).Path.Should().Be("M3 0.5V1H3V1.5");
        plan.Edges.Single(edge => edge.ChildId == helper).Should().Match<TreeEdge>(edge => edge.Pulsing && edge.Path == "M3 0.5V1H1V1.5");
    }

    [Theory]
    [InlineData("Lead", "ArrowDown", "Helper")]
    [InlineData("Helper", "ArrowDown", "Under helper")]
    [InlineData("Under helper", "ArrowDown", "Scout")]
    [InlineData("Scout", "ArrowUp", "Under helper")]
    [InlineData("Writer", "ArrowDown", "Writer")]
    [InlineData("Lead", "ArrowUp", "Lead")]
    [InlineData("Helper", "ArrowRight", "Under helper")]
    [InlineData("Scout", "ArrowRight", "Scout")]
    [InlineData("Under helper", "ArrowLeft", "Helper")]
    [InlineData("Lead", "ArrowLeft", "Lead")]
    [InlineData("Under helper", "Home", "Lead")]
    [InlineData("Lead", "End", "Writer")]
    public void Arrow_keys_move_through_the_tree_as_in_any_tree_view(string from, string key, string to)
    {
        var plan = TreeLayout.Plan(Crew());
        Guid Id(string name) => plan.Places.Single(place => place.Bot.Name == name).Bot.Id;

        TreeLayout.Move(plan, Id(from), key).Should().Be(Id(to));
    }

    [Fact]
    public void A_node_is_read_out_with_what_it_is_doing_what_it_has_for_you_and_its_cap()
    {
        var bot = new CrewBot(lead, "Lead", "Sample", [], AvatarState.Thinking, "Thinking", Attention.WaitingForYou, Cap: (3, 5));

        TreeLayout.Announce(bot).Should().Be("Lead, thinking, waiting for you, 3 of 5 bots beneath it");
        TreeLayout.Move(TreePlan.Empty, null, "ArrowDown").Should().BeNull();
    }

    #endregion

    #region Activity

    [Fact]
    public void A_run_leads_with_its_outcome_and_says_how_it_started()
    {
        var run = new ActivityRun(Guid.NewGuid(), lead, "Lead", RunOutcome.Finished, RunStart.Routine, "Write the brief.\nKeep it short.", start, "/", TimeSpan.FromSeconds(48), null, "Every weekday at 08:00");

        ActivityWords.Outcome(run).Should().Be("Finished");
        ActivityWords.Line(run).Should().Be("Lead. Routine: Every weekday at 08:00: Write the brief.");
        ActivityWords.Started(run with { Start = RunStart.Message }).Should().Be("You asked");
        ActivityWords.Started(run with { Start = RunStart.Call }).Should().Be("Called from outside");
        ActivityWords.Timing(run, "Mon 5 Jan, 08:00").Should().Be("Mon 5 Jan, 08:00, took 48 seconds");
        ActivityWords.NewRuns(3).Should().Be("Show 3 new runs");
        ActivityWords.Confirm(true, 1).Should().Be("Approve this call?");
        ActivityWords.Confirm(false, 3).Should().Be("Deny these 3 calls?");
    }

    #endregion

    #region Private Methods

    private static TimelineEvent[] Run() =>
    [
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1"), lead, TimelineKind.Step, "Thought", TimelineState.Done, start, TimeSpan.FromSeconds(4)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), lead, TimelineKind.Delegation, "Handed work", TimelineState.Done, start.AddSeconds(4), TimeSpan.FromSeconds(30), null, helper),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e3"), helper, TimelineKind.Tool, "Ran a command", TimelineState.Live, start.AddSeconds(6)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e4"), lead, TimelineKind.Step, "Answered", TimelineState.Done, start.AddSeconds(40), TimeSpan.FromSeconds(2)),
    ];

    private static CrewBot[] Crew() =>
    [
        new(lead, "Lead", "Sample",
        [
            new(helper, "Helper", "Sample", [new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a5"), "Under helper", "Sample", [])], Receiving: true),
            new(scout, "Scout", "Sample", []),
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a4"), "Writer", "Sample", []),
        ]),
    ];

    #endregion
}
