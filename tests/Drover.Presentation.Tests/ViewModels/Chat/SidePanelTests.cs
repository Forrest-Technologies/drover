namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The summary and the screen beside a conversation, against the fake server: every state each can be in, with its
/// sentence and its one next step, and the answer a person gives at the screen.
/// </summary>
public sealed class SidePanelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    private static readonly string space = ChatBench.Space;

    private static readonly string mainThread = $"{space}/bots/{botId}/main-thread";

    private static readonly string summary = $"{space}/threads/{threadId}/summary";

    private static readonly string sandbox = $"{space}/bots/{botId}/sandbox";

    private static readonly BotSummary marshal = new(botId, "Marshal", "marshal", "Sample instructions", Guid.Empty, "sample-default", "active");

    private readonly FakeTimeProvider time = new();

    private readonly ChatBench bench;

    #endregion

    #region Constructors

    public SidePanelTests()
    {
        bench = new(time: time);
        bench.Server.Answer("PUT", mainThread, new ThreadSummary(threadId, botId, null, false, DateTimeOffset.UnixEpoch, null, IsMain: true));
    }

    #endregion

    #region Public Methods

    public void Dispose() => bench.Dispose();

    #endregion

    #region Summary

    [Fact]
    public async Task A_written_summary_says_what_it_covers_and_when_it_was_written()
    {
        var written = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        bench.Server.Answer("GET", summary, new ConversationSummary("A sample summary.", 12, written));
        var panel = Summary();

        panel.State.Should().Be(SummaryPanelState.Loading);
        panel.Hint.Should().Be("Loading the summary.");
        await panel.ParametersSet();

        panel.State.Should().Be(SummaryPanelState.Ready);
        panel.Text.Should().Be("A sample summary.");
        panel.Facts.Should().Be($"Covers 12 messages · written {ScheduleWords.Moment(written)}");
        panel.StaysNote.Should().BeNull();
    }

    [Theory]
    [InlineData(1, true, "Covers 1 message · written ")]
    [InlineData(1, false, "Covers 1 messages")]
    [InlineData(7, false, "Covers 7 messages")]
    public async Task The_facts_of_a_summary_count_its_messages(int covered, bool dated, string facts)
    {
        bench.Server.Answer("GET", summary, new ConversationSummary("A sample summary.", covered, dated ? DateTimeOffset.UnixEpoch : null));
        var panel = Summary();

        await panel.ParametersSet();

        panel.Facts.Should().StartWith(facts);
    }

    [Theory]
    [InlineData(true, SummaryPanelState.NoneYet, "No summary yet. Drover writes one when this conversation is long enough to need it, and rewrites it as the conversation goes on.")]
    [InlineData(false, SummaryPanelState.ServerWritesNone, "This server does not write summaries. Its operator has them turned off.")]
    public async Task A_conversation_with_no_summary_says_why(bool serverWrites, SummaryPanelState state, string hint)
    {
        bench.Server.Answer("GET", summary, new ConversationSummary(" ", 0, null, serverWrites));
        var panel = Summary();

        await panel.ParametersSet();

        (panel.State, panel.Hint).Should().Be((state, hint));
    }

    [Fact]
    public async Task A_summary_the_server_no_longer_rewrites_says_that_it_stays_as_it_is()
    {
        bench.Server.Answer("GET", summary, new ConversationSummary("A sample summary.", 3, null, ServerWrites: false));
        var panel = Summary();

        await panel.ParametersSet();

        panel.State.Should().Be(SummaryPanelState.Ready);
        panel.StaysNote.Should().Be("This server no longer writes summaries, so this one stays as it is.");
    }

    [Fact]
    public async Task A_summary_that_fails_to_load_says_why_loads_on_a_second_try_and_is_read_again_when_the_conversation_moved_on()
    {
        bench.Server.Answer("GET", summary, HttpStatusCode.InternalServerError);
        var panel = Summary();

        await panel.ParametersSet();
        panel.State.Should().Be(SummaryPanelState.Failed);
        SummaryPanelViewModel.FailedTitle.Should().Be("Drover could not load the summary");
        panel.Problem.Should().NotBeNullOrWhiteSpace();

        bench.Server.Answer("GET", summary, new ConversationSummary("A sample summary.", 3, null));
        await panel.Load();
        panel.State.Should().Be(SummaryPanelState.Ready);

        await panel.ParametersSet();
        panel.Version = 1;
        await panel.ParametersSet();
        bench.Server.Sent("GET", summary).Should().HaveCount(3, "the same version is not read twice");
        bench.Server.Sent("PUT", mainThread).Should().ContainSingle("the conversation is found once");
    }

    #endregion

    #region Screen

    [Theory]
    [InlineData(false, false, "none", false, false, ScreenPanelState.NoProvider, "No sandbox provider is set up", "Open the status page", "/status")]
    [InlineData(true, true, "running", false, false, ScreenPanelState.NoScreenButTerminal, "Marshal has no screen", "Open its terminal", "/bots/0196f1a0-0000-7000-8000-000000000004/sandbox")]
    [InlineData(true, false, "none", false, false, ScreenPanelState.NoScreen, "Marshal has no screen", "Open its settings", "/bots/0196f1a0-0000-7000-8000-000000000004")]
    [InlineData(true, false, "none", false, true, ScreenPanelState.NoSandbox, "Marshal has no sandbox", "Open its settings", "/bots/0196f1a0-0000-7000-8000-000000000004")]
    [InlineData(true, true, "running", false, true, ScreenPanelState.NoScreen, "Marshal has no screen", "Open its settings", "/bots/0196f1a0-0000-7000-8000-000000000004")]
    public async Task A_bot_with_no_screen_to_show_says_why_and_where_the_next_step_is(
        bool available, bool hasSandbox, string state, bool desktop, bool desktopOffered, ScreenPanelState expected, string title, string action, string href)
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(available, hasSandbox, state, null, desktop, false, desktopOffered));
        await using var panel = Screen();

        await panel.Open();

        panel.State.Should().Be(expected);
        (panel.EmptyTitle, panel.EmptyAction, panel.EmptyHref).Should().Be((title, action, href));
        panel.EmptyCause.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_bot_with_a_screen_says_what_its_sandbox_is_doing_and_links_to_the_terminal()
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, true, true));
        await using var panel = Screen();

        panel.State.Should().Be(ScreenPanelState.Loading);
        ScreenPanelViewModel.Loading.Should().Be("Loading the screen.");
        await panel.Open();

        panel.State.Should().Be(ScreenPanelState.Screen);
        panel.SandboxState.Should().Be(SandboxWords.State("running"));
        panel.BotWorking.Should().BeTrue();
        (ScreenPanelViewModel.TerminalText, panel.TerminalHref).Should().Be(("Open terminal and power controls", Addresses.BotTerminal(botId)));
    }

    [Fact]
    public async Task Closing_the_screen_during_its_first_read_does_not_throw()
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, true, true));
        var answer = bench.Server.Hold("GET", sandbox, ignoreCancellation: true);
        var panel = Screen();
        var open = panel.Open();
        await ChatBench.Eventually(() => bench.Server.Sent("GET", sandbox).Should().ContainSingle());

        await panel.DisposeAsync();
        answer.SetResult();

        var ending = () => open;
        await ending.Should().NotThrowAsync("the answer was already on its way when the person closed the screen");
        time.Advance(TimeSpan.FromSeconds(30));
        bench.Server.Sent("GET", sandbox).Should().ContainSingle("a closed screen reads nothing more");
    }

    [Fact]
    public async Task Reading_the_state_again_after_the_screen_closed_does_not_throw()
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, true, true));
        var panel = Screen();
        await panel.Open();
        await panel.DisposeAsync();

        var reading = () => panel.Refresh();

        await reading.Should().NotThrowAsync("the screen's surface can say the sandbox changed after the panel closed");
        bench.Server.Sent("GET", sandbox).Should().ContainSingle();
    }

    [Fact]
    public async Task Someone_who_may_not_use_the_screen_is_told_who_does_and_the_state_is_not_read_again()
    {
        bench.Server.Answer("GET", sandbox, HttpStatusCode.Forbidden);
        await using var panel = Screen();

        await panel.Open();
        time.Advance(TimeSpan.FromSeconds(30));

        panel.State.Should().Be(ScreenPanelState.NotPermitted);
        panel.NotPermittedHint.Should().Be("Owners and admins of this space watch and use its bots' screens. You can still talk to Marshal.");
        bench.Server.Sent("GET", sandbox).Should().ContainSingle();
    }

    [Fact]
    public async Task A_screen_that_fails_to_load_says_why_and_recovers_when_the_state_is_read_again()
    {
        bench.Server.Answer("GET", sandbox, HttpStatusCode.InternalServerError);
        await using var panel = Screen();

        await panel.Open();
        panel.State.Should().Be(ScreenPanelState.Failed);
        (panel.EmptyTitle, panel.EmptyAction, panel.EmptyHref).Should().Be(("Drover could not load the screen", "Try again", null));

        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, false, true));
        time.Advance(TimeSpan.FromSeconds(10));

        await ChatBench.Eventually(() => panel.State.Should().Be(ScreenPanelState.Screen, "a read that works clears a first load that failed"));
    }

    [Fact]
    public async Task The_state_is_read_every_ten_seconds_while_the_panel_is_open_and_no_longer_once_it_closed()
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "paused", null, true, false, true));
        var panel = Screen();
        await panel.Open();

        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, true, true));
        time.Advance(TimeSpan.FromSeconds(10));
        await ChatBench.Eventually(() => panel.BotWorking.Should().BeTrue("the bot's work shows"));

        await panel.DisposeAsync();
        var read = bench.Server.Sent("GET", sandbox).Count();
        time.Advance(TimeSpan.FromSeconds(30));

        bench.Server.Sent("GET", sandbox).Should().HaveCount(read);
    }

    [Fact]
    public async Task A_person_holding_the_screen_hands_it_back_from_the_panel()
    {
        Guid? handed = null;
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, false, true));
        bench.Server.Answer("POST", $"{space}/tool-calls/{callId}/handback", new RunSummary(Guid.NewGuid(), threadId, botId, Guid.NewGuid(), "running", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        await using var panel = Screen();
        panel.Takeover = new(callId, "Sign in with your passkey.", TakeoverPhase.Holding);
        panel.HandedBack += id => handed = id;
        await panel.Open();

        panel.Held.Should().Be(panel.Takeover);
        panel.IsHeld.Should().BeTrue();
        await panel.HandBack(done: true, note: null);

        handed.Should().Be(callId);
        panel.HandProblem.Should().BeNull();
        bench.Server.Sent("POST", $"{space}/tool-calls/{callId}/handback").Should().ContainSingle().Which.Should().Contain("\"done\":true");
    }

    [Fact]
    public async Task An_answer_given_already_counts_as_handed_back_and_one_that_was_not_sent_says_why()
    {
        List<Guid> handed = [];
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, false, true));
        bench.Server.Answer("POST", $"{space}/tool-calls/{callId}/handback", HttpStatusCode.InternalServerError);
        await using var panel = Screen();
        panel.Takeover = new(callId, "Sign in with your passkey.", TakeoverPhase.Holding);
        panel.HandedBack += handed.Add;
        await panel.Open();

        await panel.HandBack(done: false, note: "No code.");
        panel.HandProblem.Should().StartWith("Your answer was not sent.");
        handed.Should().BeEmpty();

        bench.Server.Answer("POST", $"{space}/tool-calls/{callId}/handback", HttpStatusCode.Conflict);
        await panel.HandBack(done: false, note: "No code.");

        handed.Should().Equal(callId);
        panel.HandProblem.Should().BeNull();
    }

    [Fact]
    public async Task A_request_that_no_longer_waits_shows_no_controls_at_the_screen()
    {
        bench.Server.Answer("GET", sandbox, new BotSandboxStatus(true, true, "running", null, true, false, true));
        await using var panel = Screen();
        panel.Takeover = new(callId, "Sign in with your passkey.", TakeoverPhase.Done);

        await panel.Open();

        panel.Held.Should().BeNull();
        panel.IsHeld.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private SummaryPanelViewModel Summary()
    {
        var panel = bench.Make<SummaryPanelViewModel>();
        panel.Bot = marshal;
        return panel;
    }

    private ScreenPanelViewModel Screen()
    {
        var panel = bench.Make<ScreenPanelViewModel>();
        panel.Bot = marshal;
        return panel;
    }

    #endregion
}
