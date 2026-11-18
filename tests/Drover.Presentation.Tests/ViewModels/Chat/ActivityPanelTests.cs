namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The activity rail against event logs recorded from a real server. The three-bot log: a stub model had Marshal hand
/// research to Scout, Scout look over its crew and hand a check to Checker, Checker ask to check the time (approved),
/// then Marshal ask to check the time too (approved). The single-lane log: Marshal checked the time, then asked to run
/// a command, which was approved and failed because that server's sandbox key was not a real one. The cases are the
/// web client's, asked of the panel's view model and of the one for a node's details.
/// </summary>
public sealed class ActivityPanelTests : IDisposable
{
    #region Private Fields

    private const string ThreeBots = "three-bots-two-levels";

    private const string OneBot = "run-with-two-tools-and-an-approval";

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid marshal = Guid.Parse("01a1e4c5-76f6-733b-937a-479c62f1ce50");

    private static readonly Guid scout = Guid.Parse("01a1e4c5-788a-7364-b7be-858e25e736da");

    private static readonly Guid checker = Guid.Parse("01a1e4c5-78cb-7812-af8d-160a1fb98db2");

    private static readonly Guid threadId = Guid.Parse("01a1e4c6-01eb-7c42-8ae5-6aa0a46413f6");

    private static readonly string space = ChatBench.Space;

    private static readonly BotSummary[] crew =
    [
        new(marshal, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active"),
        new(scout, "Scout", "scout", "Sample instructions", connectionId, "sample-default", "active", marshal),
        new(checker, "Checker", "checker", "Sample instructions", connectionId, "sample-default", "active", scout),
    ];

    private readonly ChatBench bench = new();

    private readonly List<ActivityPanelViewModel> opened = [];

    #endregion

    #region Properties

    private FakeServer Server => bench.Server;

    #endregion

    #region Public Methods

    public void Dispose()
    {
        foreach (var panel in opened)
        {
            panel.Dispose();
        }

        bench.Dispose();
    }

    #endregion

    #region Live And Replayed

    [Fact]
    public async Task A_run_watched_live_and_the_same_run_opened_after_it_ended_lay_out_the_same_nodes_in_the_same_order()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = log[0].RunId;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        var live = Server.Stream($"{space}/runs/{root}/events");
        var watched = Open();
        await ChatBench.Eventually(() => watched.Tree.Should().NotBeNull());
        watched.State.Should().Be(ActivityPanelState.Loading);
        ActivityPanelViewModel.Loading.Should().Be("Loading the run.");

        foreach (var stored in log)
        {
            await live.Send(stored);
        }

        await ChatBench.Eventually(() => watched.Facts.Should().Contain("Finished"));
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        var replay = Server.Stream($"{space}/runs/{root}/events");
        foreach (var each in log)
        {
            await replay.Send(each);
        }

        var reopened = Open();
        await ChatBench.Eventually(() => reopened.Facts.Should().Contain("Finished"));

        Sentences(reopened).Should().Equal(Sentences(watched));
        reopened.Model.Lanes.Should().Equal(watched.Model.Lanes);
        reopened.Model.Nodes.Select(node => (node.Lane, node.Row, node.BranchLane, node.Event.Kind, node.Event.State, node.Event.Summary))
            .Should().Equal(watched.Model.Nodes.Select(node => (node.Lane, node.Row, node.BranchLane, node.Event.Kind, node.Event.State, node.Event.Summary)));
        Sentences(reopened).Should().HaveCount(17);
    }

    [Fact]
    public async Task A_three_bot_run_has_a_lane_for_each_bot_and_each_hand_off_leaves_and_comes_back_at_its_own_node()
    {
        var page = await OpenFinished(ThreeBots);

        page.Model.Lanes.Select(lane => lane.Name).Should().Equal("Marshal", "Scout", "Checker");
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
        page.Model.Nodes.Where(node => node.BranchLane is not null)
            .Select(node => (Timelines.Sentence(page.Model, node)[..30], Column(node)))
            .Should().Equal(
                ("Marshal: Handed work to Scout.", "1 / 2"),
                ("Scout: Handed work to Checker.", "2 / 3"),
                ("Scout: Got the answer from Che", "2 / 3"),
                ("Marshal: Got the answer from S", "1 / 2"));
        page.Model.Nodes.Should().NotContain(node => node.Event.State == TimelineState.Live, "nothing moves once the run is over");
    }

    #endregion

    #region Details

    [Fact]
    public async Task A_nodes_details_are_the_stored_call_its_arguments_as_stored_its_whole_result_and_how_long_it_took()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{OneBot}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{OneBot}.messages.json");
        var page = await OpenFinished(OneBot, crew[..1]);
        var command = RunEventTypes.Read<ToolRequested>(log.Last(stored => stored.Type == RunEventTypes.ToolRequested));
        var completed = RunEventTypes.Read<ToolCompleted>(log.Last(stored => stored.Type == RunEventTypes.ToolCompleted));
        var storedResult = messages.SelectMany(message => message.Parts).Single(part => part.ToolCallId == command.ToolCallId && part.Kind == "tool_result");

        var detail = Choose(page, "Marshal: Ran a command. Failed");

        detail.IsCommand.Should().BeTrue();
        $"{RunMomentDetailViewModel.Prompt}{detail.CommandLine}".Should().StartWith("$ ls -la /home/user && echo done");
        RunMomentDetailViewModel.TerminalName.Should().Be("Command and its output");
        JsonElement.DeepEquals(JsonElement.Parse(detail.Card!.Result!), JsonElement.Parse(storedResult.Result!))
            .Should().BeTrue("the result is the stored one, whole");
        detail.Facts.Should().Equal(("Took", Words.Duration(TimeSpan.FromMilliseconds(completed.DurationMs))));
        detail.ResultIsCut.Should().BeFalse("the conversation holds this call's whole result");

        detail = Choose(page, "Marshal: Asked to run a command. Done");

        detail.Asks.Should().BeFalse();
        detail.Answered.Should().Be("This was approved.");
        detail.ApprovalShowsArguments.Should().BeTrue();
        detail.Card!.Arguments.Should().Contain("\"command\": \"ls -la /home/user && echo done\"", "the arguments are shown as the log stored them");
    }

    [Fact]
    public async Task A_commands_output_reads_as_a_terminal_with_error_output_marked_in_words()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{OneBot}.json");
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

        var detail = Choose(page, "Marshal: Ran a command.");

        detail.IsCommand.Should().BeTrue();
        detail.ToolPartTitles.Should().StartWith("Command");
        detail.CommandLine.Should().Be("ls -la /home/user && echo done\n");
        detail.Card!.Output.Select(piece => (piece.Stream, piece.Text)).Should().Equal(
            ("stdout", "total 0\ndrwxr-xr-x user\n"), ("stderr", "ls: cannot open one entry\n"));
        OutputWords.ErrorStream.Should().Be("stderr", "error output is marked in words as well as color");
    }

    [Fact]
    public async Task What_a_model_wrote_is_shown_as_text_and_never_as_markup()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        log = [.. log.Select(stored => stored.Type switch
        {
            RunEventTypes.MessageCompleted => stored with { Data = Hostile(stored.Data, "Scout and Checker found") },
            RunEventTypes.DelegationCompleted => stored with { Data = Hostile(stored.Data, "Three sources") },
            _ => stored,
        })];
        var page = await OpenFinished(ThreeBots, log: log);

        var answer = Choose(page, "Marshal: Got the answer from Scout. Done.");
        answer.AnswerText.Should().Contain("Plain words after the tags.");
        var last = Detail(page, page.Model.Nodes[^1]);
        last.StepText.Should().Contain("Plain words after the tags.");

        // The native details draw these from the markdown node model, which has no node for raw markup.
        foreach (var text in new[] { answer.AnswerText!, last.StepText! })
        {
            Flatten(MarkdownDocument.Parse(text)).Should().NotContain(node => node is ImageNode, "a child's answer and a bot's words are untrusted text");
            Flatten(MarkdownDocument.Parse(text)).OfType<ElementNode>().Select(element => element.Name).Should().NotContain(["script", "iframe", "img"]);
        }
    }

    #endregion

    #region A Run Still Going

    [Fact]
    public async Task A_log_that_stops_while_a_bot_waits_shows_the_wait_and_the_work_above_it_as_waiting_never_as_stopped()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var waits = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested).Seq;
        var page = await OpenLive(ThreeBots, log.Where(stored => stored.Seq <= waits + 2));

        await ChatBench.Eventually(() => Sentences(page).Should().Contain("Checker: Asked to check the time. Waiting for you."));
        Sentences(page).Should().Contain(["Checker: Check the time. Not run yet.", "Scout: Handed work to Checker. Waiting for you.", "Marshal: Handed work to Scout. Waiting for you."])
            .And.NotContain(sentence => sentence.Contains("Cancelled"));
        page.Model.Nodes.Should().NotContain(node => node.Event.State == TimelineState.Live, "while the run waits for a person, nothing moves");
    }

    [Fact]
    public async Task A_child_bots_waiting_call_is_answered_from_its_node_by_its_name()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var asked = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested);
        var call = RunEventTypes.Read<ToolApprovalRequested>(asked).ToolCallId;
        var root = log[0].RunId;
        Server.Answer("POST", $"{space}/tool-calls/{call}/decision", new RunSummary(asked.RunId, threadId, checker, root, "running", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var page = await OpenLive(ThreeBots, log.Where(stored => stored.Seq <= asked.Seq + 2));
        await ChatBench.Eventually(() => Sentences(page).Should().Contain("Checker: Asked to check the time. Waiting for you."));

        var detail = Choose(page, "Checker: Asked to check the time.");

        detail.Asks.Should().BeTrue();
        detail.Question.Should().Be("Checker asks to check the time.");
        (RunMomentDetailViewModel.Approve, RunMomentDetailViewModel.Deny).Should().Be(("Approve", "Deny"));
        await detail.Answer(approved: true);

        Server.Sent("POST", $"{space}/tool-calls/{call}/decision").Should().ContainSingle().Which.Should().Contain("\"approved\":true");
        detail.Deciding.Should().BeTrue("an answer that went through is not offered again before the run moves on");
        await detail.Answer(approved: true);
        Server.Sent("POST", $"{space}/tool-calls/{call}/decision").Should().ContainSingle("a second press sends no second decision");
    }

    [Fact]
    public async Task An_answer_given_already_is_said_in_words_and_can_be_given_again()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var asked = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested);
        var call = RunEventTypes.Read<ToolApprovalRequested>(asked).ToolCallId;
        Server.Refuse("POST", $"{space}/tool-calls/{call}/decision", HttpStatusCode.Conflict, "This call is not waiting for an answer any more.");
        var page = await OpenLive(ThreeBots, log.Where(stored => stored.Seq <= asked.Seq + 2));
        await ChatBench.Eventually(() => Sentences(page).Should().Contain("Checker: Asked to check the time. Waiting for you."));
        var detail = Choose(page, "Checker: Asked to check the time.");

        await detail.Answer(approved: false);

        detail.Problem.Should().Be("This request was answered already.");
        detail.Deciding.Should().BeFalse();
    }

    [Fact]
    public async Task Trying_again_after_the_runs_stream_failed_watches_the_same_run_again()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        Server.Answer("GET", $"{space}/runs/{root}/events", HttpStatusCode.NotFound);

        var page = Open();
        await ChatBench.Eventually(() => page.State.Should().Be(ActivityPanelState.Failed));
        ActivityPanelViewModel.FailedTitle.Should().Be("Drover could not load this run");

        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log.Take(5))
        {
            await events.Send(stored);
        }

        await page.Load();

        await ChatBench.Eventually(() => page.State.Should().Be(ActivityPanelState.Run, "trying again opens the run's stream again"));
    }

    [Fact]
    public async Task A_command_that_is_running_carries_the_traveling_line_and_says_so_in_words()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{OneBot}.json");
        var started = log.Last(stored => stored.Type == RunEventTypes.ToolStarted).Seq;
        var page = await OpenLive(OneBot, log.Where(stored => stored.Seq <= started), crew[..1]);

        await ChatBench.Eventually(() => page.Model.Nodes.Where(node => node.Event is { State: TimelineState.Live, Kind: TimelineKind.Tool }).Should().ContainSingle()
            .Which.Should().Match<TimelineNode>(node => Timelines.Sentence(page.Model, node) == "Marshal: Running a command. Working."));
        page.Model.Nodes.Where(node => node.Event.State == TimelineState.Live).Should().ContainSingle("only the call being worked on moves");
    }

    #endregion

    #region States

    [Fact]
    public async Task A_conversation_with_no_run_yet_says_so()
    {
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new List<MessageResponse>());

        var page = Open();

        await ChatBench.Eventually(() => page.State.Should().Be(ActivityPanelState.NothingRan));
        ActivityPanelViewModel.NothingRanTitle.Should().Be("Nothing has run in this conversation yet");
        page.NothingRanCause.Should().Be("When Marshal answers a message, what it does shows here as it happens.");
        (page.NothingRanAction, page.NothingRanHref).Should().Be(("Write to Marshal", Addresses.Talk(marshal)));
    }

    [Fact]
    public async Task While_the_run_loads_the_rail_says_so_and_a_failure_says_what_to_do()
    {
        var held = Server.Hold("PUT", $"{space}/bots/{marshal}/main-thread");

        var page = Open();

        page.State.Should().Be(ActivityPanelState.Loading);
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", HttpStatusCode.InternalServerError);
        held.SetResult();
        await ChatBench.Eventually(() => page.State.Should().Be(ActivityPanelState.Failed));
        page.Problem.Should().NotBeNullOrWhiteSpace();
        ActivityPanelViewModel.TryAgain.Should().Be("Try again");
    }

    [Fact]
    public async Task A_run_whose_first_events_are_no_longer_kept_says_so()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");

        var page = await OpenFinished(ThreeBots, log: [.. log.Where(stored => stored.Seq > 20)]);

        page.LostNote.Should().Be("The start of this run is no longer kept, so its earliest events are not shown.");
        page.Model.Lanes.Select(lane => lane.Name).Should().Contain("A bot this run no longer names");
    }

    [Fact]
    public async Task The_list_form_is_one_choice_away_and_holds_the_same_nodes_in_the_same_order()
    {
        var page = await OpenFinished(ThreeBots);
        var sentences = Sentences(page);
        var redraws = 0;
        page.PropertyChanged += (_, _) => redraws++;

        page.AsList = true;

        redraws.Should().Be(1);
        (ActivityPanelViewModel.FormName, ActivityPanelViewModel.TimelineForm, ActivityPanelViewModel.ListForm).Should().Be(("Show the run as", "Timeline", "List"));
        Sentences(page).Should().Equal(sentences, "both forms draw the one model");
    }

    #endregion

    #region An Earlier Run

    [Fact]
    public async Task A_runs_last_reply_opens_that_run_and_the_newest_one_is_one_link_away()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = log[0].RunId;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/runs/{root}", new RunSummary(root, threadId, marshal, root, "completed", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log)
        {
            await events.Send(stored);
        }

        var page = Open(runId: root);

        await ChatBench.Eventually(() => page.Facts.Should().StartWith("An earlier run · Finished"));
        (ActivityPanelViewModel.NewestText, page.NewestHref).Should().Be(("Show the newest run", Addresses.Activity(marshal)));
        page.Model.Nodes.Should().HaveCount(17);

        page.Version = 3;
        await page.ParametersSet();
        Server.Sent("PUT", $"{space}/bots/{marshal}/main-thread").Should().ContainSingle("pointed at one run, the panel stays on it");
    }

    [Fact]
    public async Task A_run_asked_for_by_a_bot_beneaths_reply_shows_the_whole_tree_it_belongs_to()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ThreeBots}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{ThreeBots}.messages.json");
        var root = log[0].RunId;
        var child = RunEventTypes.Read<DelegationStarted>(log.First(stored => stored.Type == RunEventTypes.DelegationStarted)).ChildRunId;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/runs/{child}", new RunSummary(child, threadId, scout, root, "completed", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        Server.Answer("GET", $"{space}/runs/{root}", new RunSummary(root, threadId, marshal, root, "completed", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log)
        {
            await events.Send(stored);
        }

        var page = Open(runId: child);

        await ChatBench.Eventually(() => page.Facts.Should().StartWith("An earlier run · Finished"));
        page.Tree!.RootRunId.Should().Be(root);
        page.Model.Lanes.Select(lane => lane.Name).Should().Equal("Marshal", "Scout", "Checker");
    }

    #endregion

    #region Private Methods

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, marshal, null, false, DateTimeOffset.UnixEpoch, activeRunId, IsMain: true);

    private ActivityPanelViewModel Open(IReadOnlyList<BotSummary>? bots = null, Guid? runId = null)
    {
        var panel = bench.Make<ActivityPanelViewModel>();
        opened.Add(panel);
        (panel.Bot, panel.Bots, panel.RunId) = ((bots ?? crew)[0], bots ?? crew, runId);
        _ = panel.ParametersSet();
        return panel;
    }

    private async Task<ActivityPanelViewModel> OpenFinished(string fixture, IReadOnlyList<BotSummary>? bots = null, List<RunEventResponse>? log = null)
    {
        log ??= ChatBench.Load<List<RunEventResponse>>($"{fixture}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{fixture}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in log)
        {
            await events.Send(stored);
        }

        var page = Open(bots);
        await ChatBench.Eventually(() =>
        {
            page.State.Should().Be(ActivityPanelState.Run);
            page.Facts.Should().Contain("Finished");
        });
        return page;
    }

    private async Task<ActivityPanelViewModel> OpenLive(string fixture, IEnumerable<RunEventResponse> prefix, IReadOnlyList<BotSummary>? bots = null)
    {
        var messages = ChatBench.Load<List<MessageResponse>>($"{fixture}.messages.json");
        var root = messages.First(message => message.RunId is not null).RunId!.Value;
        Server.Answer("PUT", $"{space}/bots/{marshal}/main-thread", Thread(root));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        var events = Server.Stream($"{space}/runs/{root}/events");
        foreach (var stored in prefix)
        {
            await events.Send(stored);
        }

        var page = Open(bots);
        await ChatBench.Eventually(() => page.State.Should().Be(ActivityPanelState.Run));
        return page;
    }

    private static List<string> Sentences(ActivityPanelViewModel page) =>
        [.. page.Model.Nodes.Select(node => Timelines.Sentence(page.Model, node))];

    // The details of the node whose sentence starts so, as the timeline hands them to the control under the node.
    private static RunMomentDetailViewModel Choose(ActivityPanelViewModel page, string sentenceStart) =>
        Detail(page, page.Model.Nodes.Single(node => Timelines.Sentence(page.Model, node).StartsWith(sentenceStart, StringComparison.Ordinal)));

    private static RunMomentDetailViewModel Detail(ActivityPanelViewModel page, TimelineNode node)
    {
        var detail = new RunMomentDetailViewModel
        {
            Tree = page.Tree!,
            Moment = page.MomentOf(node)!,
            BotName = page.NameOf(node.Lane),
            ChildName = node.BranchLane is { } lane ? page.NameOf(lane) : string.Empty,
            Results = page.Results,
            Decide = page.Decide,
        };
        detail.ParametersSet();
        return detail;
    }

    // The columns a hand-off's line spans, as the web client's grid names them.
    private static string Column(TimelineNode node) =>
        $"{Math.Min(node.Lane, node.BranchLane!.Value) + 1} / {Math.Max(node.Lane, node.BranchLane.Value) + 1}";

    // A stored message or answer whose text a model filled with markup.
    private static string Hostile(string data, string marker) =>
        data.Replace(marker, $"<script>alert(1)</script><img src=x onerror=alert(1)><iframe src=x></iframe> Plain words after the tags. {marker}", StringComparison.Ordinal);

    private static IEnumerable<MarkdownNode> Flatten(IEnumerable<MarkdownNode> nodes) =>
        nodes.SelectMany(node => Flatten(node switch
        {
            ElementNode element => element.Children,
            LinkNode link => link.Children,
            _ => [],
        }).Prepend(node));

    #endregion
}
