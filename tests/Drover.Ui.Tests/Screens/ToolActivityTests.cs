namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The conversation's tool cards and approvals, fed with an event log recorded from a real server: a stub model had
/// Atlas check the time (allowed), then ask to run a command (granted "ask"); the command was approved and failed,
/// because that server's sandbox key was not a real one. Output streaming is added to that log where a test says so:
/// no sandbox ran there, so no output was recorded.
/// </summary>
public sealed class ToolActivityTests : ScreenTest
{
    #region Private Fields

    private const string Fixture = "run-with-two-tools-and-an-approval";

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private readonly List<RunEventResponse> log = Load<List<RunEventResponse>>($"{Fixture}.json");

    private readonly List<MessageResponse> messages = Load<List<MessageResponse>>($"{Fixture}.messages.json");

    #endregion

    #region Constructors

    public ToolActivityTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { new ModelConnectionSummary(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null) });
        Server.Answer("GET", $"{space}/bots", new[] { new BotSummary(botId, "Atlas", "atlas", "Sample instructions", connectionId, "sample-default", "active") });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) });
    }

    #endregion

    #region Properties

    private Guid RunId => log[0].RunId;

    private long Waiting => log.Single(stored => stored.Type == RunEventTypes.RunWaiting).Seq;

    private long CommandStarted => log.Last(stored => stored.Type == RunEventTypes.ToolStarted).Seq;

    private Guid CommandCall => RunEventTypes.Read<ToolRequested>(log.Last(stored => stored.Type == RunEventTypes.ToolRequested)).ToolCallId;

    #endregion

    #region Live

    [Fact]
    public async Task Each_call_is_a_card_after_the_words_that_made_it_and_the_command_waits_for_an_answer()
    {
        var (page, events) = WatchActiveRun();

        await Feed(events, 1, Waiting);
        page.WaitForAssertion(() => page.Find(".dv-composer-frame").ClassList.Should().NotContain("dv-state-tool", "nothing moves while the run waits"));

        var turns = page.FindAll("article");
        turns.Select(turn => turn.GetAttribute("aria-label")).Should().Equal("You said", "Atlas said", "Atlas said");
        turns[1].QuerySelector(".dv-markdown")!.TextContent.Trim().Should().Be("Let me check the time first.");
        var timeCard = turns[1].QuerySelector(".dv-tool .dv-disclosure-toggle")!;
        timeCard.TextContent.Should().Contain("Checked the time").And.Contain("Done");
        timeCard.GetAttribute("aria-expanded").Should().Be("false");
        turns[1].QuerySelector(".dv-tool .dv-badge-ok")!.TextContent.Should().Be("Done", "the state is in words, not only in green");
        turns[2].QuerySelector(".dv-markdown")!.TextContent.Trim().Should().Be("I have the time. Next I will list the files in the sandbox.");

        var ask = turns[2].QuerySelector("section.dv-tool-ask")!;
        var title = ask.QuerySelector("h2")!;
        ask.GetAttribute("aria-labelledby").Should().Be(title.Id);
        title.TextContent.Should().Be("Atlas asks to run a command");
        ask.QuerySelector(".dv-tool-ask-summary")!.TextContent.Should().Be("ls -la /home/user && echo done");
        var answers = ask.QuerySelectorAll("button");
        answers.Select(button => button.TextContent.Trim()).Should().Equal("Approve", "Deny");
        answers.Should().OnlyContain(button => button.GetAttribute("aria-describedby") == title.Id && !button.HasAttribute("disabled"));
        page.FindAll(".dv-message-state").Should().BeEmpty("the bot is not thinking while it waits for a person");
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop");
        page.FindAll("main [role=status] .dv-note").Should().BeEmpty("the card says what is waiting; no notice repeats it");
        Spoken(page).Should().Contain("Atlas asks to run a command. Waiting for you.");
    }

    [Fact]
    public async Task Approving_sends_the_answer_and_the_card_follows_the_call_to_its_end()
    {
        Server.Answer("POST", $"{space}/tool-calls/{CommandCall}/decision", Run("waiting"));
        var (page, events) = WatchActiveRun();
        await Feed(events, 1, Waiting);
        page.WaitForAssertion(() => page.Find(".dv-composer-frame").ClassList.Should().NotContain("dv-state-tool", "nothing moves while the run waits"));

        await page.Find("section.dv-tool-ask button.dv-button-primary").ClickAsync(new());

        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/tool-calls/{CommandCall}/decision").Should().ContainSingle()
            .Which.Should().Contain("\"approved\":true"));
        page.FindAll("section.dv-tool-ask button").Should().OnlyContain(button => button.HasAttribute("disabled"), "an answer is sent once");

        await Feed(events, Waiting + 1, CommandStarted);
        page.WaitForAssertion(() => page.FindAll(".dv-tool .dv-disclosure-toggle")[^1].TextContent.Should().Contain("Running a command").And.Contain("Running"));
        page.FindAll(".dv-tool section")[^1].ClassList.Should().Contain("dv-state-tool", "a line travels the card's top edge while the tool runs");
        page.FindAll("section.dv-tool-ask").Should().BeEmpty();

        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        await Feed(events, CommandStarted + 1, log[^1].Seq);
        await events.End();

        page.WaitForAssertion(() => page.Find(".dv-composer button").TextContent.Trim().Should().Be("Send"));
        var commandCard = page.FindAll(".dv-tool")[^1];
        commandCard.QuerySelector(".dv-disclosure-toggle")!.TextContent.Should().Contain("Ran a command").And.Contain("Failed");
        commandCard.QuerySelector("section")!.ClassList.Should().Contain("dv-tool-ended-failed").And.NotContain("dv-state-tool");
        commandCard.QuerySelector(".dv-badge-danger")!.TextContent.Should().Be("Failed");
        page.FindAll("article").Select(turn => turn.QuerySelector(".dv-markdown")?.TextContent.Trim()).Should().Equal(
            null, "Let me check the time first.", "I have the time. Next I will list the files in the sandbox.", "That is what the tools gave back.");
    }

    [Fact]
    public async Task Denying_sends_the_answer_and_an_answer_already_given_is_said_in_words()
    {
        Server.Refuse("POST", $"{space}/tool-calls/{CommandCall}/decision", HttpStatusCode.Conflict, "This call is not waiting for an answer any more.");
        var (page, events) = WatchActiveRun();
        await Feed(events, 1, Waiting);
        page.WaitForAssertion(() => page.Find(".dv-composer-frame").ClassList.Should().NotContain("dv-state-tool", "nothing moves while the run waits"));

        await page.FindAll("section.dv-tool-ask button")[1].ClickAsync(new());

        page.WaitForAssertion(() => page.Find("section.dv-tool-ask [role=alert]").TextContent.Trim().Should().Be("This request was answered already."));
        Server.Sent("POST", $"{space}/tool-calls/{CommandCall}/decision").Should().ContainSingle().Which.Should().Contain("\"approved\":false");
        page.FindAll("section.dv-tool-ask button").Should().OnlyContain(button => !button.HasAttribute("disabled"), "a failed answer can be given again");
    }

    [Fact]
    public async Task Output_streams_into_the_open_card_with_error_output_marked_in_words_and_is_read_out_once_per_state()
    {
        Server.Answer("POST", $"{space}/tool-calls/{CommandCall}/decision", Run("waiting"));
        var (page, events) = WatchActiveRun();
        await Feed(events, 1, CommandStarted);
        page.WaitForAssertion(() => page.FindAll(".dv-tool .dv-badge").Select(badge => badge.TextContent).Should().Equal("Done", "Running"));
        List<string> heard = [Spoken(page)];

        await page.InvokeAsync(() => page.FindAll(".dv-tool .dv-disclosure-toggle")[^1].Click());
        page.WaitForAssertion(() => page.FindAll(".dv-tool")[^1].TextContent.Should().Contain("No output yet."));

        // Not recorded: what a sandbox would have printed. Added after the recorded tool.started.
        var seq = CommandStarted;
        ToolOutput[] printed = [new(CommandCall, "stdout", "total 4\n"), new(CommandCall, "stdout", "drwxr-x--- user\n"), new(CommandCall, "stderr", "ls: slow disk\n"), new(CommandCall, "stdout", "done\n")];
        foreach (var piece in printed)
        {
            await events.Send(new RunEventResponse(++seq, RunId, RunEventTypes.ToolOutput, RunEventTypes.Write(piece), DateTimeOffset.UnixEpoch));
            page.WaitForAssertion(() => page.Find(".dv-tool-output-text").TextContent.Should().EndWith(piece.Text));
            heard.Add(Spoken(page));
        }

        var output = page.Find(".dv-tool-output");
        output.GetAttribute("role").Should().Be("group");
        output.GetAttribute("aria-label").Should().Be("Output");
        output.GetAttribute("tabindex").Should().Be("0", "long output scrolls inside the card, by keyboard too");
        page.Find(".dv-tool-output-text").TextContent.Should().Be("total 4\ndrwxr-x--- user\nstderrls: slow disk\ndone\n");
        page.Find(".dv-tool-stderr .dv-tool-stream").TextContent.Should().Be("stderr", "error output is marked in words, not only in red");
        heard.Distinct().Should().ContainSingle("output is never read out piece by piece").Which.Should().Contain("Running a command.");

        var completed = new ToolCompleted(CommandCall, "completed", false, """{"exit_code":0,"stdout":"total 4"}""", 120);
        await events.Send(new RunEventResponse(++seq, RunId, RunEventTypes.ToolCompleted, RunEventTypes.Write(completed), DateTimeOffset.UnixEpoch));

        page.WaitForAssertion(() => page.Find(".dv-tool-exit").TextContent.Should().Be("Exit code 0"));
        page.FindAll(".dv-tool .dv-disclosure-toggle")[^1].TextContent.Should().Contain("Ran a command").And.Contain("Done");
        Spoken(page).Should().Contain("Ran a command. Done.");
    }

    #endregion

    #region History

    [Fact]
    public void Reopening_a_finished_thread_shows_the_same_cards_from_its_runs_tool_events()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", Load<List<RunEventResponse>>($"{Fixture}.tool-events.json"));

        var page = RenderThread();

        page.WaitForAssertion(() => page.FindAll(".dv-tool").Should().HaveCount(2));
        var toggles = page.FindAll(".dv-tool .dv-disclosure-toggle");
        toggles[0].TextContent.Should().Contain("Checked the time").And.Contain("Done");
        toggles[1].TextContent.Should().Contain("Ran a command").And.Contain("Failed").And.Contain("ls -la /home/user && echo done");
        page.FindAll("section.dv-tool-ask").Should().BeEmpty();
        Spoken(page).Should().NotContain("Checked the time", "calls that ended before the page opened are history, not news")
            .And.NotContain("Ran a command");

        toggles[1].Click();

        var opened = page.FindAll(".dv-tool")[1];
        opened.QuerySelector(".dv-disclosure-toggle")!.GetAttribute("aria-expanded").Should().Be("true");
        opened.QuerySelectorAll("h3").Select(heading => heading.TextContent).Should().Equal("Arguments", "Result");
        opened.QuerySelectorAll(".dv-code-body")[1].TextContent.Should().Contain("The server's sandbox provider key does not work.", "the stored result is shown whole");
    }

    [Fact]
    public void A_thread_whose_tool_events_cannot_be_read_still_shows_its_calls_as_the_messages_tell_them()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages);
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", HttpStatusCode.InternalServerError);

        var page = RenderThread();

        page.WaitForAssertion(() => page.FindAll(".dv-tool").Should().HaveCount(2));
        page.FindAll(".dv-tool .dv-disclosure-title").Select(title => title.TextContent).Should().Equal("Checked the time", "Ran a command");
        page.FindAll("main [role=alert] .dv-note").Should().BeEmpty("the conversation reads without the log");
    }

    #endregion

    #region Private Methods

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), json)!;

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, botId, "Sample thread", false, DateTimeOffset.UnixEpoch, activeRunId);

    private RunSummary Run(string status) =>
        new(RunId, threadId, botId, RunId, status, null, null, 1, 0, new(0, 0, 0, 0), null, null);

    private static string Spoken(IRenderedComponent<Threads> page) => page.Find("[aria-live=polite]").TextContent;

    private IRenderedComponent<Threads> RenderThread()
    {
        Navigation.NavigateTo($"/threads/{threadId}");
        return Render<Threads>(parameters => parameters.Add(page => page.ThreadId, threadId));
    }

    // The recorded run is under way when the page opens: the person's message is stored, and the run's events follow.
    private (IRenderedComponent<Threads> Page, LiveEvents Events) WatchActiveRun()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(RunId));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", messages.Take(1).ToList());
        Server.Answer("GET", $"{space}/threads/{threadId}/tool-events", new List<RunEventResponse>());
        var events = Server.Stream($"{space}/runs/{RunId}/events");
        var page = RenderThread();
        page.WaitForElement("article");
        return (page, events);
    }

    /// <summary>Sends the recorded events numbered <paramref name="from"/> to <paramref name="to"/>.</summary>
    private async Task Feed(LiveEvents events, long from, long to)
    {
        foreach (var stored in log.Where(stored => stored.Seq >= from && stored.Seq <= to))
        {
            await events.Send(stored);
        }
    }

    #endregion
}
