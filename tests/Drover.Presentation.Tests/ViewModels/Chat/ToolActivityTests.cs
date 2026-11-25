namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The conversation's tool cards and approvals, fed with an event log recorded from a real server: a stub model had
/// Marshal check the time (allowed), then ask to run a command (granted "ask"); the command was approved and failed,
/// because that server's sandbox key was not a real one. Output streaming is added to that log where a test says so:
/// no sandbox ran there, so no output was recorded. The cases are the web client's as far as the cards go; what its
/// screen adds around them (the turns, the composer, the notices) is tested with the conversation.
/// </summary>
public sealed class ToolActivityTests
{
    #region Private Fields

    private const string Fixture = "run-with-two-tools-and-an-approval";

    private readonly List<RunEventResponse> log = ChatBench.Load<List<RunEventResponse>>($"{Fixture}.json");

    private readonly List<MessageResponse> messages = ChatBench.Load<List<MessageResponse>>($"{Fixture}.messages.json");

    #endregion

    #region Properties

    private Guid RunId => log[0].RunId;

    private long Waiting => log.Single(stored => stored.Type == RunEventTypes.RunWaiting).Seq;

    private long CommandStarted => log.Last(stored => stored.Type == RunEventTypes.ToolStarted).Seq;

    private Guid CommandCall => RunEventTypes.Read<ToolRequested>(log.Last(stored => stored.Type == RunEventTypes.ToolRequested)).ToolCallId;

    #endregion

    #region Live

    [Fact]
    public void Each_call_is_a_card_after_the_words_that_made_it_and_the_command_waits_for_an_answer()
    {
        var conversation = Live(log.Where(stored => stored.Seq <= Waiting));

        var said = conversation.Where(message => !message.FromPerson).ToList();
        said[0].Text.Trim().Should().Be("Let me check the time first.");
        var timeCard = Shown(said[0].Tools.Single());
        (timeCard.Card.Label, timeCard.Card.State, timeCard.Open).Should().Be(("Checked the time", "Done", false));
        timeCard.StateKind.Should().Be(BadgeKind.Ok, "the state is in words, not only in green");
        said[1].Text.Trim().Should().Be("I have the time. Next I will list the files in the sandbox.");
        var ask = Shown(said[1].Tools.Single());
        ask.IsAsk.Should().BeTrue();
        ask.AskTitle.Should().Be("Marshal asks to run a command");
        ask.Summary.Should().Be("ls -la /home/user && echo done");
        ask.Deciding.Should().BeFalse();
        new ToolAnnouncer().Next(conversation.AllTools(), "Marshal").Should().Contain("Marshal asks to run a command. Waiting for you.");
    }

    [Fact]
    public async Task Approving_sends_the_answer_and_the_card_follows_the_call_to_its_end()
    {
        List<(Guid, bool)> answers = [];
        var card = Shown(Live(log.Where(stored => stored.Seq <= Waiting)).AllTools().Last());
        card.Decide = (id, approved) =>
        {
            answers.Add((id, approved));
            return Task.FromResult<string?>(null);
        };

        await card.Answer(approved: true);

        answers.Should().Equal((CommandCall, true));
        card.Deciding.Should().BeTrue("an answer is sent once");

        card.Card = Live(log.Where(stored => stored.Seq <= CommandStarted)).AllTools().Last();
        card.ParametersSet();
        (card.Card.Label, card.Card.State).Should().Be(("Running a command", "Running"));
        card.Running.Should().BeTrue("a line travels around the card while the tool runs");
        card.IsAsk.Should().BeFalse();

        card.Card = Live(log, messages).AllTools().Last();
        card.ParametersSet();
        (card.Card.Label, card.Card.State).Should().Be(("Ran a command", "Failed"));
        (card.EndedEdge, card.Running).Should().Be(("DvDanger", false));
        card.StateKind.Should().Be(BadgeKind.Danger);
    }

    [Fact]
    public void Output_streams_into_the_open_card_with_error_output_marked_in_words_and_is_read_out_once_per_state()
    {
        var announcer = new ToolAnnouncer();
        List<RunEventResponse> soFar = [.. log.Where(stored => stored.Seq <= CommandStarted)];
        var conversation = Live(soFar);
        conversation.AllTools().Select(tool => tool.State).Should().Equal("Done", "Running");
        List<string?> heard = [announcer.Next(conversation.AllTools(), "Marshal")];
        var card = Shown(conversation.AllTools().Last());
        card.Open = true;
        card.ShowsQuiet.Should().BeTrue();
        ToolCallCardViewModel.Quiet.Should().Be("No output yet.");

        // Not recorded: what a sandbox would have printed. Added after the recorded tool.started.
        var seq = CommandStarted;
        ToolOutput[] printed = [new(CommandCall, "stdout", "total 4\n"), new(CommandCall, "stdout", "drwxr-x--- user\n"), new(CommandCall, "stderr", "ls: slow disk\n"), new(CommandCall, "stdout", "done\n")];
        foreach (var piece in printed)
        {
            soFar.Add(new(++seq, RunId, RunEventTypes.ToolOutput, RunEventTypes.Write(piece), DateTimeOffset.UnixEpoch));
            conversation = Live(soFar);
            card.Card = conversation.AllTools().Last();
            card.ParametersSet();
            string.Concat(card.Card.Output.Select(part => part.Text)).Should().EndWith(piece.Text);
            heard.Add(announcer.Next(conversation.AllTools(), "Marshal"));
        }

        ToolCallCardViewModel.OutputTitle.Should().Be("Output");
        string.Concat(card.Card.Output.Select(part => part.Stream is OutputWords.ErrorStream ? $"{OutputWords.ErrorStream}{part.Text}" : part.Text))
            .Should().Be("total 4\ndrwxr-x--- user\nstderrls: slow disk\ndone\n", "error output is marked in words, not only in red");
        heard.Where(sentence => sentence is not null).Should().ContainSingle("output is never read out piece by piece").Which.Should().Contain("Running a command.");

        var completed = new ToolCompleted(CommandCall, "completed", false, """{"exit_code":0,"stdout":"total 4"}""", 120);
        soFar.Add(new(++seq, RunId, RunEventTypes.ToolCompleted, RunEventTypes.Write(completed), DateTimeOffset.UnixEpoch));
        conversation = Live(soFar);
        card.Card = conversation.AllTools().Last();
        card.ParametersSet();
        card.ExitLine.Should().Be("Exit code 0");
        (card.Card.Label, card.Card.State).Should().Be(("Ran a command", "Done"));
        announcer.Next(conversation.AllTools(), "Marshal").Should().Contain("Ran a command. Done.");
    }

    #endregion

    #region History

    [Fact]
    public void Reopening_a_finished_thread_shows_the_same_cards_from_its_runs_tool_events()
    {
        var history = ToolHistory.From(ChatBench.Load<List<RunEventResponse>>($"{Fixture}.tool-events.json"));

        var conversation = ChatTimeline.Merge(messages, null).ToChatMessages(history);

        var cards = conversation.AllTools().Select(tool => Shown(tool)).ToList();
        cards.Should().HaveCount(2);
        (cards[0].Card.Label, cards[0].Card.State).Should().Be(("Checked the time", "Done"));
        (cards[1].Card.Label, cards[1].Card.State, cards[1].Summary).Should().Be(("Ran a command", "Failed", "ls -la /home/user && echo done"));
        cards.Should().NotContain(card => card.IsAsk);
        new ToolAnnouncer().Next(conversation.AllTools(), "Marshal").Should().BeNull("calls that ended before the page opened are history, not news");
        cards[1].Open = true;
        cards[1].PartTitles.Should().Equal("Arguments", "Result");
        cards[1].Card.Result.Should().Contain("The server's sandbox provider key does not work.", "the stored result is read whole and said in words");
    }

    [Fact]
    public void A_thread_whose_tool_events_cannot_be_read_still_shows_its_calls_as_the_messages_tell_them()
    {
        var conversation = ChatTimeline.Merge(messages, null).ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty);

        conversation.AllTools().Select(tool => tool.Label).Should().Equal("Checked the time", "Ran a command");
    }

    #endregion

    #region Private Methods

    // The conversation while the recorded run is under way: the person's message is stored, and the run's events follow.
    private IReadOnlyList<ChatMessage> Live(IEnumerable<RunEventResponse> events, IEnumerable<MessageResponse>? stored = null)
    {
        var run = RunReducer.Apply(RunView.Start(RunId), events);
        return ChatTimeline.Merge(stored ?? messages.Take(1), run).ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.With(run), run);
    }

    private static ToolCallCardViewModel Shown(ToolCard card)
    {
        var model = new ToolCallCardViewModel { Card = card, BotName = "Marshal" };
        model.ParametersSet();
        return model;
    }

    #endregion
}
