namespace Drover.Ui.Tests;

/// <summary>
/// The timeline in the interface kit: how a run is laid out, where the keys go, what each node is called, what moves,
/// and the list form. The first of these came with the starter it was built from.
/// </summary>
public sealed class TimelineTests : BunitContext
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Constructors

    public TimelineTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Layout And Words

    [Fact]
    public void A_run_is_laid_out_with_a_lane_for_each_bot_in_the_order_they_joined()
    {
        var model = Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], Words());

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
        var model = Timelines.Build([], Words().Reverse());

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
        var model = Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], Words());

        Timelines.Move(model, from, key).Should().Be(to);
    }


    [Fact]
    public void An_answer_comes_back_across_from_the_lane_the_work_went_to_and_says_where_it_came_from()
    {
        TimelineEvent[] events =
        [
            .. Words().Take(3),
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e5"), lead, TimelineKind.Answer, "Got the answer", TimelineState.Done, start.AddSeconds(34), null, null, helper),
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e6"), lead, TimelineKind.Waiting, "Asked to run a command", TimelineState.Denied, start.AddSeconds(36)),
        ];
        var model = Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], events);

        model.Nodes.Select(node => (node.Lane, node.BranchLane)).Should().Equal((0, null), (0, 1), (1, null), (0, 1), (0, null));
        Timelines.Sentence(model, model.Nodes[3]).Should().Be("Lead: Got the answer from Helper. Done.");
        Timelines.Sentence(model, model.Nodes[4]).Should().Be("Lead: Asked to run a command. Denied.");
    }

    #endregion

    #region The Component

    [Fact]
    public void The_timeline_is_one_tab_stop_whose_nodes_are_named_as_sentences_and_open_in_place()
    {
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Run())
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet.")
            .Add(view => view.Detail, node => $"<p id=\"detail\">{node.Event.Label}</p>"));

        var nodes = timeline.FindAll("button.dv-timeline-node");
        nodes.Select(node => node.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1");
        nodes.Select(node => node.GetAttribute("aria-label")).Should().Equal(
            "Lead: Thought. Done, took 4 seconds.",
            "Lead: Handed work to Helper. Done, took 30 seconds.",
            "Helper: Ran a command. Working.");
        timeline.FindAll(".dv-timeline-branch").Should().ContainSingle();
        timeline.FindAll("button.dv-state-tool").Should().ContainSingle("only the tool that is running carries the traveling line");

        timeline.FindAll("button.dv-timeline-node")[0].KeyDown("ArrowDown");
        timeline.FindAll("button.dv-timeline-node").Select(node => node.GetAttribute("tabindex")).Should().Equal("-1", "0", "-1");
        timeline.FindAll("[data-arrow-item]").Should().HaveCount(3, "only the nodes take the arrow keys, never what is open inside them");

        timeline.FindAll("button.dv-timeline-node")[2].Click();

        timeline.Find("#detail").TextContent.Should().Be("Ran a command");
        timeline.FindAll("button.dv-timeline-node")[2].GetAttribute("aria-expanded").Should().Be("true");
    }


    [Fact]
    public void An_event_that_arrives_late_with_an_earlier_start_does_not_move_the_open_node_or_the_tab_stop()
    {
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Run())
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet.")
            .Add(view => view.Detail, node => $"<p class=\"detail\">{node.Event.Label}</p>"));
        timeline.FindAll("button.dv-timeline-node")[2].Click();

        var late = new TimelineEvent(Guid.NewGuid(), lead, TimelineKind.Rule, "A rule fired", TimelineState.Done, start.AddSeconds(-1));
        timeline.Render(set => set.Add(view => view.Model, Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], [.. RunEvents(), late])));

        timeline.Find(".detail").TextContent.Should().Be("Ran a command");
        timeline.Find("button.dv-timeline-node[tabindex='0']").GetAttribute("aria-label").Should().StartWith("Helper: Ran a command");
    }


    [Fact]
    public void Arrow_keys_still_move_focus_on_the_timeline_after_its_parent_renders_again()
    {
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Run())
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet."));

        timeline.Render(set => set.Add(view => view.Model, Run()));
        timeline.FindAll("button.dv-timeline-node")[0].KeyDown("ArrowDown");

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>();
    }


    [Fact]
    public void The_timeline_as_a_list_holds_the_same_nodes_and_an_empty_one_says_so()
    {
        var list = Render<Timeline>(set => set
            .Add(view => view.Model, Run())
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet.")
            .Add(view => view.AsList, true));

        list.FindAll("ol > li").Should().HaveCount(3);
        list.Find("ol").TextContent.Should().Contain("Lead: Handed work to Helper. Done, took 30 seconds.");

        list.Render(set => set.Add(view => view.Model, TimelineModel.Empty));

        list.Find(".dv-timeline-hint").TextContent.Should().Be("Nothing has run in this conversation yet.");
    }


    [Fact]
    public void The_nodes_are_a_list_in_time_order_and_only_the_working_node_carries_a_live_state()
    {
        TimelineEvent[] events =
        [
            .. RunEvents(),
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e7"), lead, TimelineKind.Step, "Thinking", TimelineState.Live, start.AddSeconds(8)),
        ];
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], events))
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet."));

        timeline.Find("[role=group]").GetAttribute("aria-label").Should().Be("What happened in this run");
        timeline.FindAll("[role=list] > [role=listitem]").Select(item => item.QuerySelector("button")!.GetAttribute("aria-label")).Should().Equal(
            "Lead: Thought. Done, took 4 seconds.",
            "Lead: Handed work to Helper. Done, took 30 seconds.",
            "Helper: Ran a command. Working.",
            "Lead: Thinking. Working.");
        timeline.FindAll(".dv-timeline-head").Should().OnlyContain(head => head.GetAttribute("aria-hidden") == "true", "each node's sentence names its bot");
        timeline.FindAll("button.dv-state-tool").Select(node => node.GetAttribute("aria-label")).Should().Equal("Helper: Ran a command. Working.");
        timeline.Find(".dv-timeline-head .dv-avatar[aria-label^='Helper']").GetAttribute("aria-label").Should().Be("Helper, running a tool");
        timeline.FindAll(".dv-state-thinking").Should().HaveCount(2, "the thinking step's edge breathes, and so does its bot's head")
            .And.OnlyContain(element => element.Closest("button") == null || element.Closest("button")!.GetAttribute("aria-label") == "Lead: Thinking. Working.");
        timeline.FindAll(".dv-state-delegation").Should().BeEmpty("work that came back no longer passes along its branch");
        timeline.FindAll(".dv-timeline-selected").Should().BeEmpty("only the node that is open looks chosen");
    }

    [Fact]
    public void A_step_that_has_begun_to_write_holds_a_steady_edge_and_its_bot_head_says_it_writes()
    {
        TimelineEvent[] events =
        [
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e8"), lead, TimelineKind.Step, "Writing", TimelineState.Live, start) { Writing = true },
        ];
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Timelines.Build([new(lead, "Lead")], events))
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet."));

        timeline.FindAll(".dv-state-thinking").Should().BeEmpty("the breath is for thinking; writing is steady");
        timeline.Find("button.dv-timeline-node").ClassList.Should().Contain("dv-timeline-live");
        timeline.Find(".dv-timeline-head .dv-avatar").GetAttribute("aria-label").Should().Be("Lead, writing");
    }

    [Fact]
    public void Work_that_is_out_pulses_along_its_branch()
    {
        TimelineEvent[] events =
        [
            new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), lead, TimelineKind.Delegation, "Handed work", TimelineState.Live, start, null, null, helper),
        ];
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], events))
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet."));

        timeline.Find(".dv-timeline-branch").ClassList.Should().Contain("dv-state-delegation");
        timeline.Find("button.dv-timeline-node").ClassList.Should().Contain("dv-timeline-handoff", "work out with another bot is violet, not a bot thinking");
        timeline.FindAll(".dv-state-thinking, .dv-state-tool").Should().BeEmpty();
    }

    [Fact]
    public void A_long_run_shows_its_newest_events_first_and_pages_back_and_a_key_that_moves_past_the_page_shows_more()
    {
        TimelineEvent[] events =
        [
            .. Enumerable.Range(0, 450).Select(index => new TimelineEvent(
                new Guid(index, 0, 0x7000, 0x80, 0, 0, 0, 0, 0, 0, 0x01), lead, TimelineKind.Tool, $"Call {index}", TimelineState.Done, start.AddSeconds(index))),
        ];
        var timeline = Render<Timeline>(set => set
            .Add(view => view.Model, Timelines.Build([new(lead, "Lead")], events))
            .Add(view => view.Label, "What happened in this run")
            .Add(view => view.EmptyText, "Nothing has run in this conversation yet."));

        timeline.FindAll("button.dv-timeline-node").Should().HaveCount(200);
        timeline.Find("button.dv-timeline-node").GetAttribute("aria-label").Should().Be("Lead: Call 250. Done.");
        timeline.Find(".dv-timeline-earlier p").TextContent.Should().Be("250 earlier events are not shown.");

        timeline.FindAll("button").Single(button => button.TextContent.Trim() == "Show earlier events").Click();
        timeline.FindAll("button.dv-timeline-node").Should().HaveCount(400);

        timeline.Find("button.dv-timeline-node").KeyDown("Home");
        timeline.FindAll("button.dv-timeline-node").Should().HaveCount(450);
        timeline.Find("button.dv-timeline-node[tabindex='0']").GetAttribute("aria-label").Should().Be("Lead: Call 0. Done.");
        timeline.FindAll(".dv-timeline-earlier").Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static TimelineEvent[] Words() =>
    [
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1"), lead, TimelineKind.Step, "Thought", TimelineState.Done, start, TimeSpan.FromSeconds(4)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), lead, TimelineKind.Delegation, "Handed work", TimelineState.Done, start.AddSeconds(4), TimeSpan.FromSeconds(30), null, helper),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e3"), helper, TimelineKind.Tool, "Ran a command", TimelineState.Live, start.AddSeconds(6)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e4"), lead, TimelineKind.Step, "Answered", TimelineState.Done, start.AddSeconds(40), TimeSpan.FromSeconds(2)),
    ];

    private static TimelineModel Run() => Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], RunEvents());

    private static TimelineEvent[] RunEvents() =>
    [
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1"), lead, TimelineKind.Step, "Thought", TimelineState.Done, start, TimeSpan.FromSeconds(4)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), lead, TimelineKind.Delegation, "Handed work", TimelineState.Done, start.AddSeconds(4), TimeSpan.FromSeconds(30), null, helper),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e3"), helper, TimelineKind.Tool, "Ran a command", TimelineState.Live, start.AddSeconds(6)),
    ];

    #endregion
}
