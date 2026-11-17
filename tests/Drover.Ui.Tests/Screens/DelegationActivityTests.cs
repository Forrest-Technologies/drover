namespace Drover.Ui.Tests.Screens;

/// <summary>
/// Work passing between bots in the conversation, fed with event logs recorded from the run engine: Marshal handed
/// work to Scout, which looked something up and handed part of it to Checker; and Marshal handed work to Scribe, which
/// stopped to ask before writing a file. The same cards are built live and from the thread's stored events.
/// </summary>
public sealed partial class DelegationActivityTests : ScreenTest
{
    #region Private Fields

    private const string TwoLevels = "delegation-two-levels";

    private const string ChildWaits = "delegation-child-waits";

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Constructors

    public DelegationActivityTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { new ModelConnectionSummary(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null) });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) });
    }

    #endregion

    #region Live And Reopened

    [Fact]
    public async Task Handed_work_reads_the_same_live_and_after_the_conversation_is_reopened()
    {
        var log = Load<List<RunEventResponse>>($"{TwoLevels}.json");
        var (scout, checker) = Crew(log, "Scout", "Checker");
        var (page, events) = WatchActiveRun(log);

        await Feed(events, log, log.First(stored => stored.Type == RunEventTypes.DelegationStarted).Seq + 1);
        page.WaitForAssertion(() => page.Find(".dv-handoff .dv-badge").TextContent.Should().Be("Working"));
        page.Find(".dv-handoff").ClassList.Should().Contain("dv-handoff-out", "the edge is violet while the work is out");
        page.Find(".dv-handoff .dv-disclosure-title").TextContent.Should().Be("Marshal asked Scout: Find three sources on the topic.");
        page.Find($"a[href='/talk/{scout}']").GetAttribute("aria-label").Should().StartWith("Scout, thinking", "the child's visor breathes while it works");
        page.Find($"a[href='/talk/{scout}']").ParentElement!.QuerySelector(".dv-crew-wire.dv-state-delegation").Should().NotBeNull();

        Server.Answer("GET", $"{space}/threads/{threadId}/messages", Load<List<MessageResponse>>($"{TwoLevels}.messages.json"));
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        await Feed(events, log, log[^1].Seq);
        await events.End();
        page.WaitForAssertion(() => page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Send"));
        page.Find($"a[href='/talk/{scout}']").ParentElement!.QuerySelector(".dv-crew-wire")!.ClassList.Should().Contain("dv-crew-wire-back");
        var live = await page.InvokeAsync(() => OpenEveryCard(page));

        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", Load<List<RunEventResponse>>($"{TwoLevels}.tool-events.json"));
        var reopened = RenderThread();
        reopened.WaitForAssertion(() => reopened.FindAll(".dv-handoff").Should().ContainSingle("the inner card is inside the closed outer one"));

        var saved = await reopened.InvokeAsync(() => OpenEveryCard(reopened));
        Comparable(saved).Should().Be(Comparable(live));
        reopened.FindAll(".dv-crew-wire").Should().BeEmpty("nothing moves for work that ended before the page opened");

        var outer = reopened.FindAll(".dv-handoff")[0];
        outer.QuerySelector(".dv-badge")!.TextContent.Should().Be("Done");
        outer.ClassList.Should().Contain("dv-handoff-done");
        outer.QuerySelectorAll("h3").Select(heading => heading.TextContent).Should().Equal(
            ["What Scout was asked", "What Scout did", "Result", "What Checker was asked", "What Checker answered", "What Scout answered"],
            "a child's last reply is its answer, shown once, whole, at the end");
        outer.QuerySelector(".dv-handoff-task")!.TextContent.Should().Be("Find three sources on the topic.\nKeep each to one line.");
        outer.QuerySelectorAll(".dv-tool .dv-disclosure-title").Select(title => title.TextContent).Should().Equal(["test.lookup"], "Scout's own call, with the same card");
        var inner = reopened.FindAll(".dv-handoff")[1];
        inner.QuerySelector(".dv-disclosure-title")!.TextContent.Should().Be("Scout asked Checker: Check that the second source holds up.");
        inner.QuerySelector(".dv-handoff-link")!.GetAttribute("href").Should().StartWith($"/talk/{botId}/work/{checker}/");
        inner.QuerySelector(".dv-markdown")!.TextContent.Trim().Should().Be("The second source holds up.");
        reopened.FindAll(".dv-handoff .dv-markdown").Select(text => text.TextContent.Trim())
            .Should().ContainSingle(text => text.StartsWith("Three sources", StringComparison.Ordinal), "Scout's answer is not repeated in its work");
    }

    [Fact]
    public void A_childs_side_opens_beside_the_conversation_to_read_with_its_own_handed_work_as_cards()
    {
        var log = Load<List<RunEventResponse>>($"{TwoLevels}.json");
        var (scout, _) = Crew(log, "Scout", "Checker");
        var started = RunEventTypes.Read<DelegationStarted>(log.First(stored => stored.Type == RunEventTypes.DelegationStarted));
        var scoutThread = started.ChildThreadId!.Value;
        var below = log.Where(stored => stored.RunId != log[0].RunId && stored.RunId != started.ChildRunId).Select(stored => stored.RunId).ToHashSet();
        string[] own = [RunEventTypes.ToolRequested, RunEventTypes.ToolStarted, RunEventTypes.ToolCompleted, RunEventTypes.DelegationStarted, RunEventTypes.DelegationCompleted];
        string[] deeper = [.. own, RunEventTypes.MessageCompleted, RunEventTypes.RunStarted, RunEventTypes.RunCompleted];
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", Load<List<MessageResponse>>($"{TwoLevels}.messages.json"));
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", Load<List<RunEventResponse>>($"{TwoLevels}.tool-events.json"));
        Server.Answer("GET", $"{space}/threads/{scoutThread}", new ThreadSummary(scoutThread, scout, "Find three sources", false, DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", $"{space}/threads/{scoutThread}/messages", ScoutsMessages(log, started, scoutThread));
        Server.Answer("GET", $"{space}/runs/{started.ChildRunId}", new RunSummary(started.ChildRunId, scoutThread, scout, log[0].RunId, "completed", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        Server.Answer("GET", $"{space}/threads/{scoutThread}/tool-events",
            log.Where(stored => (stored.RunId == started.ChildRunId && own.Contains(stored.Type)) || (below.Contains(stored.RunId) && deeper.Contains(stored.Type))).ToList());
        Navigation.NavigateTo($"/talk/{botId}/work/{scout}/{scoutThread}");

        var page = Render<Talk>(parameters => parameters.Add(view => view.BotId, botId).Add(view => view.WorkBotId, scout).Add(view => view.WorkThreadId, scoutThread));

        page.WaitForAssertion(() => page.Find("aside .dv-handoff .dv-disclosure-title").TextContent.Should().Be("Scout asked Checker: Check that the second source holds up."));
        var side = page.Find("aside");
        side.GetAttribute("aria-label").Should().Be("Scout's side of work handed to it");
        side.QuerySelector("a.dv-shell-up")!.GetAttribute("aria-label").Should().Be("Close Scout's side");
        side.QuerySelector("a.dv-shell-up")!.GetAttribute("href").Should().Be($"/talk/{botId}");
        side.QuerySelectorAll("article").Select(turn => turn.GetAttribute("aria-label")).First().Should().Be("Marshal asked", "the task was Marshal's, not the person's");
        side.QuerySelector(".dv-talk-read")!.TextContent.Should().Be("Scout did this for Marshal. It is here to read.");
        side.QuerySelectorAll(".dv-composer").Should().BeEmpty("the child's side is there to read");
        Server.Sent("PUT", $"{space}/threads/{scoutThread}/seen").Should().BeEmpty("reading a bot's side marks nothing seen");
    }

    [Fact]
    public async Task A_childs_side_opened_while_the_work_is_out_follows_the_child_as_it_works_and_waits()
    {
        var log = Load<List<RunEventResponse>>($"{ChildWaits}.json");
        var (scribe, _) = Crew(log, "Scribe", "Unused");
        var root = log[0].RunId;
        var started = RunEventTypes.Read<DelegationStarted>(log.Single(stored => stored.Type == RunEventTypes.DelegationStarted));
        var child = started.ChildRunId;
        var scribeThread = started.ChildThreadId!.Value;
        var requested = log.First(stored => stored.RunId == child && stored.Type == RunEventTypes.ToolRequested).Seq;
        string[] own = [RunEventTypes.ToolRequested, RunEventTypes.ToolApprovalRequested, RunEventTypes.ToolStarted, RunEventTypes.ToolCompleted];
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", Load<List<MessageResponse>>($"{TwoLevels}.messages.json").Take(1).ToList());
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", new List<RunEventResponse>());
        Server.Answer("GET", $"{space}/threads/{scribeThread}", new ThreadSummary(scribeThread, scribe, "Write the notes", false, DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", $"{space}/threads/{scribeThread}/messages", new List<MessageResponse>
        {
            new(Guid.NewGuid(), scribeThread, 1, "user", child, [new MessagePartResponse("text") { Text = "Write the notes to a file." }], DateTimeOffset.UnixEpoch),
        });

        // The side is opened with the child's log cut off mid-run: its call has been asked for and has not ended.
        Server.Answer("GET", $"{space}/threads/{scribeThread}/tool-events", log.Where(stored => stored.RunId == child && stored.Seq <= requested && own.Contains(stored.Type)).ToList());
        Server.Answer("GET", $"{space}/runs/{child}", new RunSummary(child, scribeThread, scribe, root, "running", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var events = Server.Stream($"{space}/runs/{root}/events");
        Navigation.NavigateTo($"/talk/{botId}/work/{scribe}/{scribeThread}");
        var page = Render<Talk>(parameters => parameters.Add(view => view.BotId, botId).Add(view => view.WorkBotId, scribe).Add(view => view.WorkThreadId, scribeThread));
        await Feed(events, log, requested);

        page.WaitForAssertion(() => page.Find("aside .dv-tool .dv-badge").TextContent.Should().Be("Starting", "a call of a child still at work is not history"));
        page.Find("aside .dv-talk-read").TextContent.Should().Be("Scribe is doing this for Marshal.");

        await Feed(events, log, log[^1].Seq);

        page.WaitForAssertion(() => page.Find("aside section.dv-tool-ask h2").TextContent.Should().StartWith("Scribe asks to"));
        page.Find("aside section.dv-tool-ask .dv-badge").TextContent.Should().Be("Waiting for you");
        page.FindAll("aside .dv-badge").Select(badge => badge.TextContent).Should().NotContain("Stopped");
    }

    #endregion

    #region A Child Asks

    [Fact]
    public async Task A_childs_approval_opens_its_card_names_the_child_and_is_answered_from_the_parents_conversation()
    {
        var log = Load<List<RunEventResponse>>($"{ChildWaits}.json");
        var (scribe, _) = Crew(log, "Scribe", "Unused");
        var asked = RunEventTypes.Read<ToolApprovalRequested>(log.Single(stored => stored.Type == RunEventTypes.ToolApprovalRequested));
        Server.Answer("POST", $"{space}/tool-calls/{asked.ToolCallId}/decision", new RunSummary(log[0].RunId, threadId, botId, log[0].RunId, "waiting", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        var (page, events) = WatchActiveRun(log);

        await Feed(events, log, log[^1].Seq);

        page.WaitForAssertion(() => page.Find(".dv-handoff .dv-badge").TextContent.Should().Be("Waiting for you"));
        page.Find(".dv-handoff .dv-disclosure-toggle").GetAttribute("aria-expanded").Should().Be("true", "the card opens by itself so the buttons are in view");
        var ask = page.Find(".dv-handoff section.dv-tool-ask");
        var title = ask.QuerySelector("h2")!;
        title.TextContent.Should().StartWith("Scribe asks to");
        ask.GetAttribute("aria-labelledby").Should().Be(title.Id);
        var buttons = ask.QuerySelectorAll("button");
        buttons.Select(button => button.TextContent.Trim()).Should().Equal("Approve", "Deny");
        buttons.Should().OnlyContain(button => button.TagName == "BUTTON" && button.GetAttribute("aria-describedby") == title.Id && !button.HasAttribute("disabled"));
        page.Find("[aria-live=polite]").TextContent.Should().Contain("Scribe asks to");
        page.Find($"a[href='/talk/{scribe}']").GetAttribute("aria-label").Should().NotContain("thinking", "a child that waits for a person is not working");
        page.Find($"a[href='/talk/{scribe}']").ParentElement!.QuerySelector(".dv-crew-wire")!.ClassList.Should().NotContain("dv-crew-wire-back", "the work is still out");

        await ask.QuerySelector("button.dv-button-primary")!.ClickAsync(new());

        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/tool-calls/{asked.ToolCallId}/decision").Should().ContainSingle()
            .Which.Should().Contain("\"approved\":true"));
    }

    #endregion

    #region Private Methods

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), json)!;

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, botId, null, false, DateTimeOffset.UnixEpoch, activeRunId, IsMain: true);

    // The bots the recorded runs handed work to, in the order they did, named and placed in the crew.
    private (Guid First, Guid Second) Crew(List<RunEventResponse> log, string first, string second)
    {
        var started = log.Where(stored => stored.Type == RunEventTypes.DelegationStarted).Select(RunEventTypes.Read<DelegationStarted>).ToList();
        var firstId = started[0].ChildBotId;
        var secondId = started.Count > 1 ? started[1].ChildBotId : Guid.NewGuid();
        Server.Answer("GET", $"{space}/bots", new[]
        {
            new BotSummary(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active"),
            new BotSummary(firstId, first, first.ToLowerInvariant(), "Sample instructions", connectionId, "sample-default", "active", botId),
            new BotSummary(secondId, second, second.ToLowerInvariant(), "Sample instructions", connectionId, "sample-default", "active", firstId),
        });
        Server.Answer("GET", $"{space}/bots/{firstId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{space}/bots/{secondId}/threads", Array.Empty<ThreadSummary>());
        return (firstId, secondId);
    }

    // Scout's thread as the server stores it: the task Marshal wrote, then Scout's own replies.
    private static List<MessageResponse> ScoutsMessages(List<RunEventResponse> log, DelegationStarted started, Guid scoutThread) =>
    [
        new(Guid.NewGuid(), scoutThread, 1, "user", started.ChildRunId,
            [new MessagePartResponse("text") { Text = "Find three sources on the topic.\nKeep each to one line." }], DateTimeOffset.UnixEpoch),
        .. log.Where(stored => stored.RunId == started.ChildRunId && stored.Type == RunEventTypes.MessageCompleted)
            .Select(stored => RunEventTypes.Read<MessageCompleted>(stored).Message),
    ];

    private IRenderedComponent<Talk> RenderThread()
    {
        Navigation.NavigateTo($"/talk/{botId}");
        return Render<Talk>(parameters => parameters.Add(page => page.BotId, botId));
    }

    // The recorded run is under way when the page opens: the person's message is stored, and the run's events follow.
    private (IRenderedComponent<Talk> Page, LiveEvents Events) WatchActiveRun(List<RunEventResponse> log)
    {
        var runId = log[0].RunId;
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(runId));
        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", Thread(runId));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", Load<List<MessageResponse>>($"{TwoLevels}.messages.json").Take(1).ToList());
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", new List<RunEventResponse>());
        var events = Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderThread();
        page.WaitForElement("article");
        return (page, events);
    }

    private static async Task Feed(LiveEvents events, List<RunEventResponse> log, long to)
    {
        foreach (var stored in log.Where(stored => stored.Seq <= to))
        {
            await events.Send(stored);
        }
    }

    // Opens every closed card inside handed work, tool cards too, outermost first, and returns the conversation's markup.
    private static string OpenEveryCard(IRenderedComponent<Talk> page)
    {
        while (page.FindAll(".dv-handoff .dv-disclosure-toggle[aria-expanded=false]") is [var closed, ..])
        {
            closed.Click();
        }

        return page.Find(".dv-messages-column").OuterHtml;
    }

    // Two renders differ in the numbers the renderer gives handlers and elements, and in the ids each component makes
    // for its own parts, not in what a person sees or hears.
    private static string Comparable(string markup) => MadeIds().Replace(RendererIds().Replace(markup, string.Empty), "id");

    [GeneratedRegex("""\s(?:blazor:[\w:]+|id|aria-controls|aria-labelledby|aria-describedby)="[^"]*"|\s(?:blazor:elementReference)""")]
    private static partial Regex RendererIds();

    [GeneratedRegex("[0-9a-f]{32}")]
    private static partial Regex MadeIds();

    #endregion
}
