namespace Drover.Ui.Tests.Starters;

/// <summary>
/// The starters as components: what they render, what they are named for assistive technology, and what they
/// hand back. These move with a starter into the product.
/// </summary>
public sealed class StarterComponentTests : BunitContext
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Constructors

    public StarterComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region The Starter Gallery

    [Fact]
    public void The_starter_gallery_is_not_shown_outside_development()
    {
        Services.AddSingleton(new UiEnvironment(IsDevelopment: false));

        var page = Render<StarterGallery>();

        page.Find("h1").TextContent.Should().Be("Not found");
    }

    [Fact]
    public void The_starter_gallery_renders_every_starter_in_both_themes_and_says_its_content_is_sample_content()
    {
        Services.AddSingleton(new UiEnvironment(IsDevelopment: true));

        var page = Render<StarterGallery>();

        page.Find(".dv-gallery-header").TextContent.Should().Contain("samples");
        page.FindAll(".dv-gallery-section").Should().NotBeEmpty().And.OnlyContain(section =>
            section.QuerySelectorAll("[data-theme]").Length == 2 && section.QuerySelector("h2")!.TextContent.Contains('#'));
        page.FindAll("[src], [href]")
            .Select(element => element.GetAttribute("src") ?? element.GetAttribute("href"))
            .Should().OnlyContain(address => address!.StartsWith('/'));
    }

    #endregion

    #region Conversation

    [Fact]
    public void A_delegation_opens_by_itself_when_the_child_starts_waiting_for_a_person()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.Working, null, DelegationRefusal.None, "/side", 0);
        var card = Render<DelegationCard>(set => set.Add(view => view.Item, item).AddChildContent("<p id=\"work\">Helper's work</p>"));

        card.Find("button").GetAttribute("aria-expanded").Should().Be("false");
        card.Find(".dv-badge").TextContent.Should().Be("Working");
        card.FindAll(".dv-handoff-out").Should().ContainSingle();

        card.Render(set => set.Add(view => view.Item, item with { Phase = DelegationPhase.WaitingForYou }));

        card.Find("button").GetAttribute("aria-expanded").Should().Be("true");
        card.Find("#work").TextContent.Should().Be("Helper's work");
        card.Find("a.dv-handoff-link").TextContent.Should().Be("Open Helper's side of this");
    }

    [Fact]
    public void A_refused_delegation_says_why_and_shows_no_task()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.Refused, null, DelegationRefusal.TargetPaused, null, 0);
        var card = Render<DelegationCard>(set => set.Add(view => view.Item, item));

        card.Find("button").Click();

        card.Find(".dv-handoff-line").TextContent.Should().Be("Helper is paused.");
        card.FindAll(".dv-handoff-task").Should().BeEmpty();
    }

    [Fact]
    public void A_person_who_cannot_do_what_the_bot_asked_can_say_why()
    {
        string? told = null;
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in to the sample site.")
            .Add(view => view.Phase, TakeoverPhase.Holding)
            .Add(view => view.OnCouldNot, note => told = note));

        card.Find("h3").TextContent.Should().Be("You have Sample bot's screen");
        Button(card, "I could not do it").Click();
        card.Find("textarea").Input("  The site wants a code I do not have.  ");
        card.Find("form").Submit();

        told.Should().Be("The site wants a code I do not have.");
    }

    [Fact]
    public void A_new_phase_of_the_takeover_starts_without_the_last_ones_half_written_answer()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked));

        Button(card, "I cannot do it").Click();
        card.FindAll("textarea").Should().ContainSingle();

        card.Render(set => set.Add(view => view.Phase, TakeoverPhase.Holding));

        card.FindAll("textarea").Should().BeEmpty();
    }

    [Theory]
    [InlineData(PushState.Off, "Turn on notifications on this device")]
    [InlineData(PushState.On, "Turn off notifications on this device")]
    [InlineData(PushState.Blocked, null)]
    [InlineData(PushState.NotSupported, null)]
    [InlineData(PushState.NeedsHomeScreen, null)]
    public void Notifications_offer_a_button_only_when_drover_can_do_something(PushState state, string? action)
    {
        var control = Render<PushControl>(set => set.Add(view => view.State, state));

        control.Find(".dv-push-state").TextContent.Should().NotBeNullOrWhiteSpace();
        control.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal(action is null ? [] : [action]);
    }

    [Fact]
    public void Speaking_a_message_is_named_and_recording_shows_the_time_and_two_ways_out()
    {
        var voice = Render<VoiceButton>(set => set.Add(view => view.Phase, VoicePhase.Idle));
        voice.Find("button").GetAttribute("aria-label").Should().Be("Speak a message");

        voice.Render(set => set.Add(view => view.Phase, VoicePhase.Recording).Add(view => view.Elapsed, TimeSpan.FromSeconds(7)));

        voice.Find(".dv-voice-status").TextContent.Should().Be("Recording, 0:07");
        voice.Find("[role=status]").TextContent.Should().Be("Recording.");
        voice.FindAll("button").Should().HaveCount(2);
        voice.Find("[aria-label='Cancel the recording']").Should().NotBeNull();
    }

    #endregion

    #region Routines

    [Fact]
    public void A_routine_is_headed_by_its_sentence_and_only_those_who_may_change_it_get_controls()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.\nKeep it short.", "0 8 * * 1-5", "Europe/Berlin", true, start, null);
        var row = Render<RoutineRow>(set => set.Add(view => view.Routine, routine).Add(view => view.BotName, "Sample bot"));

        row.Find("h3").TextContent.Should().Be("Every weekday at 08:00, Berlin time");
        row.Find(".dv-routine-prompt").TextContent.Should().Be("Write the brief.");
        row.Find("article").GetAttribute("aria-labelledby").Should().Be(row.Find("h3").Id);
        row.FindAll("button").Should().BeEmpty();

        row.Render(set => set.Add(view => view.CanChange, true).Add(view => view.BotPaused, true));

        row.FindAll("button").Select(button => button.TextContent.Trim()).Should().Contain(["Run once now", "Change", "Remove"]);
        row.Find(".dv-routine-facts").TextContent.Should().Contain("Does not run while Sample bot is paused.").And.Contain("Has not run yet");
    }

    [Fact]
    public void Choosing_when_shows_only_that_choices_fields_and_reads_the_schedule_back()
    {
        var when = RoutineWhen.Start;
        var fields = Render<RoutineWhenFields>(set => set
            .Add(view => view.When, when)
            .Add(view => view.TimeZone, "Europe/Berlin")
            .Add(view => view.WhenChanged, value => when = value));

        fields.Find("[role=status]").TextContent.Should().Be("Reads as: Every weekday at 09:00, Berlin time.");
        fields.FindAll("fieldset").Should().BeEmpty();

        fields.Render(set => set.Add(view => view.When, when with { Rhythm = RoutineRhythm.ChosenDays, Days = [] }));

        fields.FindAll("fieldset input[type=checkbox]").Should().HaveCount(7);
        fields.Find("[role=status]").TextContent.Should().Be("Choose at least one day.");

        fields.FindAll("fieldset input[type=checkbox]")[2].Change(true);

        when.Days.Should().Equal(DayOfWeek.Wednesday);
    }

    #endregion

    #region Records

    [Fact]
    public void A_long_change_is_compared_line_by_line_in_words_and_restore_is_only_for_those_who_may_change_the_bot()
    {
        Guid? restored = null;
        var entry = new HistoryEntry(Guid.NewGuid(), HistoryActor.You, "Sam", null, "bot.updated", start, [new("instructions", "one\ntwo", "one\nthree")], true);
        var list = Render<HistoryList>(set => set
            .Add(view => view.Entries, [entry])
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.OnRestore, id => restored = id));

        list.Find("ol").GetAttribute("aria-label").Should().Be("Changes to Sample bot, newest first");
        list.Find("button").TextContent.Should().Contain("You changed the instructions");
        list.Find("button").Click();

        list.Find(".dv-diff").GetAttribute("aria-label").Should().Be("The instructions. 1 line added, 1 removed");
        list.FindAll(".dv-diff-line .dv-visually-hidden").Select(word => word.TextContent).Should().Equal(string.Empty, "Removed: ", "Added: ");
        list.FindAll("button").Select(button => button.TextContent.Trim()).Should().NotContain("Restore the version before this change");

        list.Render(set => set.Add(view => view.CanChange, true));
        Button(list, "Restore the version before this change").Click();

        restored.Should().Be(entry.Id);
    }

    [Fact]
    public void A_skill_a_bot_offers_is_saved_as_the_person_edited_it_and_not_before_it_is_complete()
    {
        SkillDraft? saved = null;
        var card = Render<SkillProposalCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Offered, new SkillDraft("Export", "When exporting.", "1. Step."))
            .Add(view => view.OnSave, draft => saved = draft));

        card.Find("h3").TextContent.Should().Be("Sample bot offers to save a skill");
        card.FindAll("textarea")[1].Input(string.Empty);

        Button(card, "Save the skill").HasAttribute("disabled").Should().BeTrue();
        card.Find(".dv-skill-offer-missing").TextContent.Should().Contain("Write the steps.");

        card.FindAll("textarea")[1].Input("1. A step the person rewrote.");
        Button(card, "Save the skill").Click();

        saved.Should().Be(new SkillDraft("Export", "When exporting.", "1. A step the person rewrote."));
    }

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

    #endregion

    #region The Bold Screens

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
        timeline.FindAll(".dv-state-tool").Should().ContainSingle("only the tool that is running carries the traveling line");

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
    public void Arrow_keys_still_move_focus_in_the_crew_after_its_parent_renders_again()
    {
        CrewBot[] crew = [new(lead, "Lead", "Sample", [new(helper, "Helper", "Sample", [])])];
        var canvas = Render<CrewCanvas>(set => set.Add(view => view.Crew, crew).Add(view => view.Label, "The crew"));

        canvas.Render(set => set.Add(view => view.Crew, [crew[0] with { Role = "Sample, again" }]).Add(view => view.SelectedId, lead));
        canvas.FindAll("[role=treeitem]")[0].KeyDown("ArrowDown");

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
    public void The_crew_is_a_tree_view_with_one_tab_stop_and_a_pulse_only_where_work_is_passing()
    {
        Guid? chosen = null;
        var under = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a5");
        var scout = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a3");
        CrewBot[] crew =
        [
            new(lead, "Lead", "Decides who does what.",
            [
                new(helper, "Helper", "Writes drafts.", [new(under, "Under helper", "Checks drafts.", [])],
                    AvatarState.Thinking, "Thinking", Attention.WaitingForYou, Receiving: true, MadeBy: "Lead"),
                new(scout, "Scout", "Finds sources.", []),
            ],
            Cap: (2, 3)),
        ];
        var canvas = Render<CrewCanvas>(set => set.Add(view => view.Crew, crew).Add(view => view.Label, "The crew").Add(view => view.OnSelect, id => chosen = id));

        canvas.Find("[role=tree]").GetAttribute("aria-label").Should().Be("The crew");
        canvas.FindAll("[role=treeitem]").Select(item => (item.GetAttribute("aria-level"), item.GetAttribute("aria-expanded"), item.GetAttribute("aria-label"), item.GetAttribute("tabindex")))
            .Should().Equal(
                ("1", "true", "Lead, 2 of 3 bots beneath it", "0"),
                ("2", "true", "Helper, thinking, waiting for you", "-1"),
                ("3", null, "Under helper", "-1"),
                ("2", null, "Scout", "-1"));
        canvas.FindAll("path.dv-state-delegation").Should().ContainSingle();
        canvas.Find(".dv-canvas-maker").TextContent.Should().Be("Made by Lead");

        canvas.FindAll("[role=treeitem]")[0].KeyDown("ArrowDown");
        canvas.FindAll("[role=treeitem]").Select(item => item.GetAttribute("tabindex")).Should().Equal("-1", "0", "-1", "-1");

        canvas.FindAll("[role=treeitem]")[1].Click();

        chosen.Should().Be(helper);

        canvas.Render(set => set.Add(view => view.Crew, [crew[0] with { Children = [crew[0].Children[1]] }]).Add(view => view.SelectedId, helper));

        canvas.FindAll("[role=treeitem][tabindex='0']").Should().ContainSingle("the tree can still be reached with Tab after the chosen bot left it")
            .Which.GetAttribute("aria-label").Should().StartWith("Lead");
    }

    [Fact]
    public void Several_waiting_calls_are_answered_together_only_after_every_sentence_is_listed()
    {
        (IReadOnlyList<Guid> Calls, bool Approved)? decided = null;
        WaitingCall[] calls =
        [
            new(Guid.NewGuid(), lead, "Lead", "Lead asks to run a command", "npm test", start),
            new(Guid.NewGuid(), helper, "Helper", "Helper asks to write a file", "/notes.md", start),
            new(Guid.NewGuid(), helper, "Helper", "Helper asks to make a bot", "Scout", start, "You approved this."),
        ];
        var list = Render<WaitingList>(set => set.Add(view => view.Calls, calls).Add(view => view.OnDecide, answer => decided = answer));

        list.FindAll("input[type=checkbox]").Should().HaveCount(2, "a call that was decided cannot be chosen");
        list.FindAll(".dv-waiting-bulk").Should().BeEmpty();

        list.FindAll("input[type=checkbox]")[0].Change(true);
        list.FindAll("input[type=checkbox]")[1].Change(true);

        Button(list, "Approve the 2 chosen").Click();

        decided.Should().BeNull("nothing is sent before the confirmation");
        list.Find(".dv-waiting-confirm").TextContent.Should().Contain("Lead asks to run a command").And.Contain("npm test")
            .And.Contain("Helper asks to write a file").And.Contain("/notes.md");

        list.FindAll("button").Last(button => button.TextContent.Trim() == "Approve the 2 chosen").Click();

        decided!.Value.Approved.Should().BeTrue();
        decided.Value.Calls.Should().Equal(calls[0].Id, calls[1].Id);
    }

    [Fact]
    public void A_confirmation_whose_calls_were_answered_elsewhere_does_not_come_back_by_itself()
    {
        (IReadOnlyList<Guid> Calls, bool Approved)? decided = null;
        var first = new WaitingCall(Guid.NewGuid(), lead, "Lead", "Lead asks to run a command", "npm test", start);
        var list = Render<WaitingList>(set => set.Add(view => view.Calls, [first]).Add(view => view.OnDecide, answer => decided = answer));
        list.Find("input[type=checkbox]").Change(true);
        Button(list, "Approve the 1 chosen").Click();

        list.Render(set => set.Add(view => view.Calls, [first with { Decided = "Approved on another device." }]));
        list.Render(set => set.Add(view => view.Calls, [new WaitingCall(Guid.NewGuid(), helper, "Helper", "Helper asks to write a file", "/notes.md", start)]));

        list.FindAll(".dv-waiting-confirm").Should().BeEmpty();
        list.FindAll("input[type=checkbox]").Should().OnlyContain(box => !box.HasAttribute("checked"));
        decided.Should().BeNull();
    }

    [Fact]
    public void One_waiting_call_is_answered_in_place_and_nothing_waiting_renders_nothing()
    {
        (IReadOnlyList<Guid> Calls, bool Approved)? decided = null;
        var call = new WaitingCall(Guid.NewGuid(), lead, "Lead", "Lead asks to run a command", "npm test", start);
        var list = Render<WaitingList>(set => set.Add(view => view.Calls, [call]).Add(view => view.OnDecide, answer => decided = answer));

        Button(list, "Deny").GetAttribute("aria-describedby").Should().Be(list.Find("h3").Id);
        Button(list, "Deny").Click();

        decided!.Value.Should().BeEquivalentTo((new[] { call.Id }, false));
        Render<WaitingList>(set => set.Add(view => view.Calls, [])).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void A_run_leads_with_its_outcome_in_words()
    {
        var run = new ActivityRun(Guid.NewGuid(), lead, "Lead", RunOutcome.Failed, RunStart.Message, "A question.", start, "/talk", null, "Lead could not reach its model.");
        var row = Render<ActivityRunRow>(set => set.Add(view => view.Run, run).Add(view => view.Open, true));

        row.Find(".dv-badge").TextContent.Should().Be("Failed");
        row.Find("button").TextContent.Should().Contain("Lead. You asked: A question.");
        row.Find(".dv-run-failure").TextContent.Should().Be("Lead could not reach its model.");
        row.Find("a.dv-run-link").GetAttribute("href").Should().Be("/talk");
    }

    #endregion

    #region Private Methods

    private static IElement Button(IRenderedComponent<IComponent> component, string text) =>
        component.FindAll("button").First(button => button.TextContent.Trim() == text);

    private static TimelineModel Run() => Timelines.Build([new(lead, "Lead"), new(helper, "Helper")], RunEvents());

    private static TimelineEvent[] RunEvents() =>
    [
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1"), lead, TimelineKind.Step, "Thought", TimelineState.Done, start, TimeSpan.FromSeconds(4)),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), lead, TimelineKind.Delegation, "Handed work", TimelineState.Done, start.AddSeconds(4), TimeSpan.FromSeconds(30), null, helper),
        new(Guid.Parse("0196f1a0-0000-7000-8000-0000000000e3"), helper, TimelineKind.Tool, "Ran a command", TimelineState.Live, start.AddSeconds(6)),
    ];

    #endregion
}
