namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A card says what it is the first time it is built, from the run events that made it: a tool card its tool and
/// state, a question what is being approved, handed work the name of the bot that took it. The recorded logs are fed
/// one event at a time, and every card the conversation shows after each event is built once and read, as the native
/// card reads its view model before it is first measured.
/// </summary>
public sealed class CardFirstBuildTests
{
    #region Private Fields

    private const string Tools = "run-with-two-tools-and-an-approval";

    private const string TwoLevels = "delegation-two-levels";

    private const string ChildWaits = "delegation-child-waits";

    private const string Lead = "Marshal";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    #endregion

    #region Tool Cards

    [Fact]
    public void A_tool_card_names_its_tool_and_its_state_the_first_time_it_is_built()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{Tools}.json");
        var stored = ChatBench.Load<List<MessageResponse>>($"{Tools}.messages.json").Take(1).ToList();
        var seen = 0;

        foreach (var to in log.Select(each => each.Seq))
        {
            foreach (var (tool, botName) in Live(log, stored, to, handover: null).AllTools(Lead))
            {
                var card = Built(tool, botName);
                card.Card.Label.Should().NotBeNullOrWhiteSpace($"the card names its tool after event {to}");
                card.Card.State.Should().NotBeNullOrWhiteSpace($"the card says its state in words after event {to}");
                seen++;
            }
        }

        seen.Should().BeGreaterThan(0, "the recorded run makes tool calls");
    }

    [Fact]
    public void A_question_says_what_is_asked_and_offers_its_answers_from_its_first_build()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{Tools}.json");
        var stored = ChatBench.Load<List<MessageResponse>>($"{Tools}.messages.json").Take(1).ToList();
        var waiting = log.Single(each => each.Type == RunEventTypes.ToolApprovalRequested).Seq;

        var ask = Built(Live(log, stored, waiting, handover: null).AllTools().Last(), Lead);

        ask.IsAsk.Should().BeTrue();
        ask.AskTitle.Should().Be("Marshal asks to run a command");
        ask.SaysWhatIsAsked.Should().BeTrue();
        ask.CanAnswer.Should().BeTrue("the question already says what Approve would approve");
    }

    [Theory]
    [InlineData("", "run a command")]
    [InlineData(Lead, "")]
    [InlineData(Lead, "   ")]
    public async Task Approve_and_deny_stay_off_until_the_question_says_who_asks_and_what_for(string botName, string request)
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{Tools}.json");
        var stored = ChatBench.Load<List<MessageResponse>>($"{Tools}.messages.json").Take(1).ToList();
        var waiting = log.Single(each => each.Type == RunEventTypes.ToolApprovalRequested).Seq;
        var asked = Live(log, stored, waiting, handover: null).AllTools().Last();
        List<bool> answers = [];
        var card = Built(asked with { Request = request }, botName);
        card.Decide = (_, approved) =>
        {
            answers.Add(approved);
            return Task.FromResult<string?>(null);
        };

        card.SaysWhatIsAsked.Should().BeFalse();
        card.CanAnswer.Should().BeFalse("Approve is never offered without saying what it approves");
        await card.Answer(approved: true);
        answers.Should().BeEmpty("an answer to an unsaid question is not sent");

        card.Card = asked;
        card.BotName = Lead;
        card.ParametersSet();
        card.CanAnswer.Should().BeTrue("the answers are offered once the question is complete");
    }

    [Fact]
    public async Task The_answers_go_off_while_one_is_on_its_way()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{Tools}.json");
        var stored = ChatBench.Load<List<MessageResponse>>($"{Tools}.messages.json").Take(1).ToList();
        var waiting = log.Single(each => each.Type == RunEventTypes.ToolApprovalRequested).Seq;
        var sent = new TaskCompletionSource<string?>();
        var card = Built(Live(log, stored, waiting, handover: null).AllTools().Last(), Lead);
        card.Decide = (_, _) => sent.Task;

        var answering = card.Answer(approved: false);

        card.CanAnswer.Should().BeFalse();
        sent.SetResult("The answer was not sent. Try again.");
        await answering;
        card.CanAnswer.Should().BeTrue("a failed answer can be given again");
    }

    #endregion

    #region Delegation Cards

    [Theory]
    [InlineData(TwoLevels, "Scout", "Checker")]
    [InlineData(ChildWaits, "Scribe", "Unused")]
    public void Handed_work_names_the_bot_that_took_it_the_first_time_it_is_built(string fixture, string first, string second)
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{fixture}.json");
        var stored = ChatBench.Load<List<MessageResponse>>($"{TwoLevels}.messages.json").Take(1).ToList();
        var names = Crew(log, first, second);
        HashSet<string> named = [];

        foreach (var to in log.Select(each => each.Seq))
        {
            foreach (var (tool, botName) in Live(log, stored, to, names).AllTools(Lead))
            {
                if (tool.Delegation is not { } work)
                {
                    Built(tool, botName).Card.Label.Should().NotBeNullOrWhiteSpace($"a call inside handed work is named after event {to}");
                    continue;
                }

                var card = new DelegationCardViewModel { Item = work, HasWork = work.Work.Count > 0 };
                card.ParametersSet();
                work.ChildName.Should().BeOneOf(first, second);
                card.Title.Should().Contain(work.ChildName, $"the card names the bot that took the work after event {to}");
                card.State.Should().NotBeNullOrWhiteSpace();
                named.Add(work.ChildName);
            }
        }

        named.Should().Contain(first);
    }

    #endregion

    #region Private Methods

    // The conversation while the recorded run is under way: what is stored, then what the run has said so far.
    private static IReadOnlyList<ChatMessage> Live(List<RunEventResponse> log, IEnumerable<MessageResponse> stored, long to, Handover? handover)
    {
        var run = RunReducer.Apply(RunView.Start(log[0].RunId), log.Where(each => each.Seq <= to));
        return ChatTimeline.Merge(stored, run).ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.With(run), run, handover: handover);
    }

    // The bots the recorded runs handed work to, in the order they did, named and placed in the crew.
    private static Handover Crew(List<RunEventResponse> log, string first, string second)
    {
        var started = log.Where(each => each.Type == RunEventTypes.DelegationStarted).Select(RunEventTypes.Read<DelegationStarted>).ToList();
        var names = new Dictionary<Guid, string> { [botId] = Lead, [started[0].ChildBotId] = first };
        if (started.Count > 1)
        {
            names[started[1].ChildBotId] = second;
        }

        return new(Lead, names, (bot, thread) => Addresses.Work(botId, bot, thread));
    }

    // The card's values set and handed over once, as a card does before its first draw.
    private static ToolCallCardViewModel Built(ToolCard tool, string botName)
    {
        var card = new ToolCallCardViewModel { Card = tool, BotName = botName };
        card.ParametersSet();
        return card;
    }

    #endregion
}
