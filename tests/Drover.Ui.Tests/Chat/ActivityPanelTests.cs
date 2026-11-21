namespace Drover.Ui.Tests.Chat;

/// <summary>
/// The activity rail against event logs recorded from a real server. The three-bot log: a stub model had Marshal hand
/// research to Scout, Scout look over its crew and hand a check to Checker, Checker ask to check the time (approved),
/// then Marshal ask to check the time too (approved). The single-lane log: Marshal checked the time, then asked to run
/// a command, which was approved and failed because that server's sandbox key was not a real one.
/// </summary>
public sealed partial class ActivityPanelTests : ScreenTest
{
    #region Private Fields

    private const string ThreeBots = "three-bots-two-levels";

    private const string OneBot = "run-with-two-tools-and-an-approval";

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid marshal = Guid.Parse("01a1e4c5-76f6-733b-937a-479c62f1ce50");

    private static readonly Guid scout = Guid.Parse("01a1e4c5-788a-7364-b7be-858e25e736da");

    private static readonly Guid checker = Guid.Parse("01a1e4c5-78cb-7812-af8d-160a1fb98db2");

    private static readonly Guid threadId = Guid.Parse("01a1e4c6-01eb-7c42-8ae5-6aa0a46413f6");

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly BotSummary[] crew =
    [
        new(marshal, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active"),
        new(scout, "Scout", "scout", "Sample instructions", connectionId, "sample-default", "active", marshal),
        new(checker, "Checker", "checker", "Sample instructions", connectionId, "sample-default", "active", scout),
    ];

    #endregion

    #region Constructors

    public ActivityPanelTests()
    {
        SignedIn();
        Services.GetRequiredService<SessionState>().Begin(Person);
    }

    #endregion

    #region Live And Replayed

    [Fact]
    public async Task A_run_watched_live_and_the_same_run_opened_after_it_ended_lay_out_the_same_nodes_in_the_same_order()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = log[0].RunId;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        var live = Server.Stream($"{space}/runs/{root}/events");
        var watched = Open();
        watched.WaitForAssertion(() => watched.Find("p[role=status]").TextContent.Should().Be("Loading the run."));

        foreach (var stored in log)
        {
            await live.Send(stored);
        }

        watched.WaitForAssertion(() => watched.Find(".dv-activity-facts").TextContent.Should().Contain("Finished"));
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        var replay = Server.Stream($"{space}/runs/{root}/events");
        foreach (var each in log)
        {
            await replay.Send(each);
        }

        var reopened = Open();
        reopened.WaitForAssertion(() => reopened.Find(".dv-activity-facts").TextContent.Should().Contain("Finished"));

        Markup(reopened.Find(".dv-timeline")).Should().Be(Markup(watched.Find(".dv-timeline")));
        Sentences(reopened).Should().HaveCount(17);
    }

    [Fact]
    public async Task A_three_bot_run_has_a_lane_for_each_bot_and_each_hand_off_leaves_and_comes_back_at_its_own_node()
    {
        var page = await OpenFinished(ThreeBots);

        page.FindAll(".dv-timeline-head").Select(head => head.TextContent.Trim()).Should().Equal("Marshal", "Scout", "Checker");
        Sentences(page).Should().Equal(
            "Marshal: Thought and wrote. Done, took under a second.",
            "Marshal: Handed work to Scout. Done, took 5 seconds.",
            "Scout: Thought and wrote. Done, took under a second.",
            "Scout: Looked over its crew. Done, took under a second.",
            "Scout: Thought. Done, took under a second.",
            "Scout: Handed work to Checker. Done, took 3 seconds.",
            "Checker: Thought and wrote. Done, took under a second.",
            "Checker: Checked the time. Done, took under a second.",
            "Checker: Asked to check the time. Done, took 2 seconds.",
            "Checker: Thought and wrote. Done, took under a second.",
            "Scout: Got the answer from Checker. Done.",
            "Scout: Thought and wrote. Done, took under a second.",
            "Marshal: Got the answer from Scout. Done.",
            "Marshal: Thought and wrote. Done, took under a second.",
            "Marshal: Checked the time. Done, took under a second.",
            "Marshal: Asked to check the time. Done, took 2 seconds.",
            "Marshal: Thought and wrote. Done, took under a second.");
        page.FindAll(".dv-timeline-item").Where(item => item.QuerySelector(".dv-timeline-branch") is not null)
            .Select(item => (item.QuerySelector("button")!.GetAttribute("aria-label")![..30], Column(item.QuerySelector(".dv-timeline-branch")!)))
            .Should().Equal(
                ("Marshal: Handed work to Scout.", "1 / 2"),
                ("Scout: Handed work to Checker.", "2 / 3"),
                ("Scout: Got the answer from Che", "2 / 3"),
                ("Marshal: Got the answer from S", "1 / 2"));
        page.FindAll("[class*=dv-state-]").Should().BeEmpty("nothing moves once the run is over");
    }

    #endregion

    #region Details

    [Fact]
    public async Task A_nodes_details_are_the_stored_call_its_arguments_as_stored_its_whole_result_and_how_long_it_took()
    {
        var log = Load<List<RunEventResponse>>($"{OneBot}.json");
        var messages = Load<List<MessageResponse>>($"{OneBot}.messages.json");
        var page = await OpenFinished(OneBot, crew[..1]);
        var command = RunEventTypes.Read<ToolRequested>(log.Last(stored => stored.Type == RunEventTypes.ToolRequested));
        var completed = RunEventTypes.Read<ToolCompleted>(log.Last(stored => stored.Type == RunEventTypes.ToolCompleted));
        var storedResult = messages.SelectMany(message => message.Parts).Single(part => part.ToolCallId == command.ToolCallId && part.Kind == "tool_result");

        await Choose(page, "Marshal: Ran a command. Failed");

        var detail = page.Find(".dv-timeline-detail");
        detail.QuerySelector(".dv-moment-terminal")!.TextContent.Should().StartWith("$ ls -la /home/user && echo done");
        detail.QuerySelector(".dv-moment-terminal")!.GetAttribute("aria-label").Should().Be("Command and its output");
        detail.QuerySelector(".dv-code-body")!.TextContent.Should().Be(
            JsonElement.Parse(storedResult.Result!).GetProperty("error").GetString(),
            "the stored result is read whole and said in words, not shown as JSON");
        detail.QuerySelectorAll(".dv-moment-fact").Select(fact => (fact.QuerySelector("dt")!.TextContent, fact.QuerySelector("dd")!.TextContent))
            .Should().Equal(("Took", Words.Duration(TimeSpan.FromMilliseconds(completed.DurationMs))));
        detail.TextContent.Should().NotContain("start of the result", "the conversation holds this call's whole result");

        await Choose(page, "Marshal: Asked to run a command. Done");

        detail = page.Find(".dv-timeline-detail");
        detail.TextContent.Should().Contain("This was approved.");
        detail.QuerySelector(".dv-code-body")!.TextContent.Should().Contain("\"command\": \"ls -la /home/user && echo done\"", "the arguments are shown as the log stored them");
    }

    [Fact]
    public async Task A_commands_output_reads_as_a_terminal_with_error_output_marked_in_words()
    {
        var log = Load<List<RunEventResponse>>($"{OneBot}.json");
        var started = log.Last(stored => stored.Type == RunEventTypes.ToolStarted);
        var call = RunEventTypes.Read<ToolStarted>(started).ToolCallId;
        RunEventResponse Output(long seq, string stream, string text) =>
            new(seq, started.RunId, RunEventTypes.ToolOutput, RunEventTypes.Write(new ToolOutput(call, stream, text)), started.CreatedAt);
        List<RunEventResponse> withOutput =
        [
            .. log.Where(stored => stored.Seq <= started.Seq),
            Output(started.Seq + 1, "stdout", "total 0\ndrwxr-xr-x user\n"),
            Output(started.Seq + 2, "stderr", "ls: cannot open one entry\n"),
            .. log.Where(stored => stored.Seq > started.Seq).Select(stored => stored with { Seq = stored.Seq + 2 }),
        ];
        var page = await OpenFinished(OneBot, crew[..1], withOutput);

        await Choose(page, "Marshal: Ran a command.");

        var terminal = page.Find(".dv-moment-terminal pre");
        terminal.TextContent.Should().StartWith("$ ls -la /home/user && echo done\ntotal 0\ndrwxr-xr-x user\nstderr: ls: cannot open one entry", "a screen reader hears the tag apart from the text");
        terminal.QuerySelector(".dv-moment-stderr .dv-moment-stream")!.TextContent.Should().Be("stderr", "error output is marked in words as well as color");
    }

    [Fact]
    public async Task What_a_model_wrote_is_shown_as_text_and_never_as_markup()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        log = [.. log.Select(stored => stored.Type switch
        {
            RunEventTypes.MessageCompleted => stored with { Data = Hostile(stored.Data, "Scout and Checker found") },
            RunEventTypes.DelegationCompleted => stored with { Data = Hostile(stored.Data, "Three sources") },
            _ => stored,
        })];
        var page = await OpenFinished(ThreeBots, log: log);

        await Choose(page, "Marshal: Got the answer from Scout. Done.");
        page.Find(".dv-timeline-detail").TextContent.Should().Contain("Plain words after the tags.");
        await page.InvokeAsync(() => page.FindAll("button.dv-timeline-node")[^1].Click());
        page.Find(".dv-timeline-detail").TextContent.Should().Contain("Plain words after the tags.");

        page.FindAll("script, img, iframe, [onerror], [onclick]").Should().BeEmpty("a child's answer and a bot's words are untrusted text");
    }

    #endregion

    #region A Run Still Going

    [Fact]
    public async Task A_log_that_stops_while_a_bot_waits_shows_the_wait_and_the_work_above_it_as_waiting_never_as_stopped()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var waits = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested).Seq;
        var page = await OpenLive(ThreeBots, log.Where(stored => stored.Seq <= waits + 2));

        page.WaitForAssertion(() => Sentences(page).Should().Contain("Checker: Asked to check the time. Waiting for you."));
        Sentences(page).Should().Contain(["Checker: Check the time. Not run yet.", "Scout: Handed work to Checker. Waiting for you.", "Marshal: Handed work to Scout. Waiting for you."])
            .And.NotContain(sentence => sentence.Contains("Cancelled"));
        page.FindAll("[class*=dv-state-]").Should().BeEmpty("while the run waits for a person, nothing moves");
    }

    [Fact]
    public async Task A_child_bots_waiting_call_is_answered_from_its_node_by_its_name()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var asked = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested);
        var call = RunEventTypes.Read<ToolApprovalRequested>(asked).ToolCallId;
        var root = log[0].RunId;
        Server.Answer("POST", $"{space}/tool-calls/{call}/decision", new RunSummary(asked.RunId, threadId, checker, root, "running", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var page = await OpenLive(ThreeBots, log.Where(stored => stored.Seq <= asked.Seq + 2));
        page.WaitForAssertion(() => Sentences(page).Should().Contain("Checker: Asked to check the time. Waiting for you."));

        await Choose(page, "Checker: Asked to check the time.");

        var detail = page.Find(".dv-timeline-detail");
        var question = detail.QuerySelector("p")!;
        question.TextContent.Should().Be("Checker asks to check the time.");
        var approve = detail.QuerySelectorAll("button").Single(button => button.TextContent.Trim() == "Approve");
        approve.GetAttribute("aria-describedby").Should().Be(question.Id);
        await page.InvokeAsync(() => page.FindAll(".dv-timeline-detail button").Single(button => button.TextContent.Trim() == "Approve").Click());

        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/tool-calls/{call}/decision").Should().ContainSingle()
            .Which.Should().Contain("\"approved\":true"));
        page.FindAll(".dv-timeline-detail button").Where(button => button.TextContent.Trim() is "Approve" or "Deny")
            .Should().OnlyContain(button => button.HasAttribute("disabled"), "an answer that went through is not offered again before the run moves on");
    }

    [Fact]
    public async Task Trying_again_after_the_runs_stream_failed_watches_the_same_run_again()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        Server.Answer("GET", $"{space}/runs/{root}/events", HttpStatusCode.NotFound);

        var page = Open();
        page.WaitForAssertion(() => page.Find("h2").TextContent.Should().Be("Drover could not load this run"));

        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log.Take(5))
        {
            await events.Send(stored);
        }

        await page.InvokeAsync(() => page.FindAll("button").Single(button => button.TextContent.Trim() == "Try again").Click());

        page.WaitForAssertion(() => page.FindAll(".dv-timeline").Should().ContainSingle("trying again opens the run's stream again"));
    }

    [Fact]
    public async Task A_command_that_is_running_carries_the_traveling_line_and_says_so_in_words()
    {
        var log = Load<List<RunEventResponse>>($"{OneBot}.json");
        var started = log.Last(stored => stored.Type == RunEventTypes.ToolStarted).Seq;
        var page = await OpenLive(OneBot, log.Where(stored => stored.Seq <= started), crew[..1]);

        page.WaitForAssertion(() => page.FindAll("button.dv-state-tool").Should().ContainSingle()
            .Which.GetAttribute("aria-label").Should().Be("Marshal: Running a command. Working."));
        page.FindAll("[class*=dv-state-]").Should().OnlyContain(
            moving => moving.Closest("button.dv-state-tool") != null || moving.Closest(".dv-timeline-head") != null,
            "only the call being worked on and the head of the bot running it move");
    }

    #endregion

    #region States

    [Fact]
    public void A_conversation_with_no_run_yet_says_so()
    {
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new List<MessageResponse>());

        var page = Open();

        page.WaitForAssertion(() => page.Find("h2").TextContent.Should().Be("Nothing has run in this conversation yet"));
        page.Find(".dv-activity").TextContent.Should().Contain("When Marshal answers a message, what it does shows here as it happens.");
        page.FindAll("a").Single(link => link.TextContent.Trim() == "Write to Marshal").GetAttribute("href").Should().Be(Addresses.Talk(marshal));
    }

    [Fact]
    public void While_the_run_loads_the_rail_says_so_and_a_failure_says_what_to_do()
    {
        var held = Server.Hold("PUT", $"{space}/bots/{marshal}/main-thread");

        var page = Open();

        page.Find("p[role=status]").TextContent.Should().Be("Loading the run.");
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", HttpStatusCode.InternalServerError);
        held.SetResult();
        page.WaitForAssertion(() => page.Find("h2").TextContent.Should().Be("Drover could not load this run"));
        page.FindAll("button").Select(button => button.TextContent.Trim()).Should().Contain("Try again");
    }

    [Fact]
    public async Task A_run_whose_first_events_are_no_longer_kept_says_so()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");

        var page = await OpenFinished(ThreeBots, log: [.. log.Where(stored => stored.Seq > 20)]);

        page.Find(".dv-note").TextContent.Should().Contain("The start of this run is no longer kept, so its earliest events are not shown.");
        page.FindAll(".dv-timeline-head").Select(head => head.TextContent.Trim()).Should().Contain("A bot this run no longer names");
    }

    [Fact]
    public async Task The_list_form_is_one_choice_away_and_holds_the_same_nodes_in_the_same_order()
    {
        var page = await OpenFinished(ThreeBots);
        var sentences = Sentences(page);

        await page.InvokeAsync(() => page.FindAll("label.dv-segment").Single(choice => choice.TextContent.Trim() == "List").QuerySelector("input")!.Change(new ChangeEventArgs { Value = "on" }));

        page.FindAll("ol.dv-timeline-list > li .dv-disclosure-title").Select(title => title.TextContent).Should().Equal(sentences);
        page.FindAll("button.dv-timeline-node").Should().BeEmpty();
    }

    #endregion

    #region In The Conversation

    [Fact]
    public async Task The_header_opens_the_newest_run_and_a_runs_last_reply_opens_that_run_beside_the_conversation()
    {
        var log = Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = log[0].RunId;
        Server.Answer("GET", $"{space}/model-connections", new[] { new ModelConnectionSummary(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null) });
        Server.Answer("GET", $"{space}/bots", crew);
        Server.Answer("GET", $"{space}/bots/{marshal}/threads", new[] { Thread(null) });
        Server.Answer("GET", $"{space}/bots/{scout}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{space}/bots/{checker}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", new List<RunEventResponse>());
        Server.Answer("GET", $"{space}/runs/{root}", new RunSummary(root, threadId, marshal, root, "completed", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log)
        {
            await events.Send(stored);
        }

        Navigation.NavigateTo($"/talk/{marshal}");
        var page = Render<Talk>(set => set.Add(talk => talk.BotId, marshal));
        page.WaitForElement("article");

        page.Find("nav[aria-label='Beside the conversation'] a").GetAttribute("href").Should().Be(Addresses.Activity(marshal));
        page.FindAll("a.dv-message-run").Should().ContainSingle("a run is offered once, at its last reply")
            .Which.GetAttribute("href").Should().Be(Addresses.Activity(marshal, root));

        Navigation.NavigateTo(Addresses.Activity(marshal, root));
        page.Render(set => set.Add(talk => talk.BotId, marshal).Add(talk => talk.ActivityRunId, root));

        page.WaitForAssertion(() => page.Find("aside").GetAttribute("aria-label").Should().Be("What Marshal and its crew did in a run"));
        page.WaitForAssertion(() => page.Find(".dv-activity-facts").TextContent.Should().StartWith("An earlier run · Finished"));
        page.Find("aside .dv-shell-up").GetAttribute("aria-label").Should().Be("Close the activity");
        page.Find("aside .dv-activity-link").GetAttribute("href").Should().Be(Addresses.Activity(marshal));
        page.FindAll("aside button.dv-timeline-node").Should().HaveCount(17);
    }

    #endregion

    #region Private Methods

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), json)!;

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, marshal, null, false, DateTimeOffset.UnixEpoch, activeRunId, IsMain: true);

    private IRenderedComponent<ActivityPanel> Open(IReadOnlyList<BotSummary>? bots = null) =>
        Render<ActivityPanel>(set => set.Add(panel => panel.Bot, (bots ?? crew)[0]).Add(panel => panel.Bots, bots ?? crew));

    private async Task<IRenderedComponent<ActivityPanel>> OpenFinished(string fixture, IReadOnlyList<BotSummary>? bots = null, List<RunEventResponse>? log = null)
    {
        log ??= Load<List<RunEventResponse>>($"{fixture}.json");
        var messages = Load<List<MessageResponse>>($"{fixture}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log)
        {
            await events.Send(stored);
        }

        var page = Open(bots);
        page.WaitForAssertion(() => page.Find(".dv-activity-facts").TextContent.Should().Contain("Finished"));
        return page;
    }

    private async Task<IRenderedComponent<ActivityPanel>> OpenLive(string fixture, IEnumerable<RunEventResponse> prefix, IReadOnlyList<BotSummary>? bots = null)
    {
        var messages = Load<List<MessageResponse>>($"{fixture}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in prefix)
        {
            await events.Send(stored);
        }

        var page = Open(bots);
        page.WaitForElement(".dv-timeline");
        return page;
    }

    private static List<string> Sentences(IRenderedComponent<ActivityPanel> page) =>
        [.. page.FindAll("button.dv-timeline-node").Select(node => node.GetAttribute("aria-label")!)];

    // Found and opened in one step, so a redraw of the live timeline cannot come between the two.
    private static Task Choose(IRenderedComponent<ActivityPanel> page, string sentenceStart) =>
        page.InvokeAsync(() => page.FindAll("button.dv-timeline-node")
            .Single(node => node.GetAttribute("aria-label")!.StartsWith(sentenceStart, StringComparison.Ordinal)).Click());

    private static string Column(IElement branch) => BranchColumn().Match(branch.GetAttribute("style")!).Groups[1].Value;

    // The renderer's own ids differ between two renders of the same thing; everything else must match.
    private static string Markup(IElement element) => AvatarIds().Replace(RendererIds().Replace(element.OuterHtml, string.Empty), "dv-avatar");

    // A stored message or answer whose text a model filled with markup.
    private static string Hostile(string data, string marker) =>
        data.Replace(marker, $"<script>alert(1)</script><img src=x onerror=alert(1)><iframe src=x></iframe> Plain words after the tags. {marker}", StringComparison.Ordinal);

    [GeneratedRegex("dv-avatar-[0-9a-f]{32}")]
    private static partial Regex AvatarIds();

    [GeneratedRegex("grid-column: ([0-9]+ / [0-9]+)")]
    private static partial Regex BranchColumn();

    [GeneratedRegex("""\s(?:blazor:[a-z]+|_bl_[0-9a-f-]+)="[^"]*"|\sblazor:elementreference="[^"]*"|\s_bl_[0-9a-f-]+""")]
    private static partial Regex RendererIds();

    #endregion
}
