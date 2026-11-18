namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Work passing between bots in the conversation, fed with event logs recorded from the run engine: Marshal handed
/// work to Scout, which looked something up and handed part of it to Checker; and Marshal handed work to Scribe, which
/// stopped to ask before writing a file. The same cards are built live and from the thread's stored events. The cases
/// are the web client's as far as the cards go; what its screen adds around them (the crew column, the composer, the
/// side that opens beside the conversation) is tested with the conversation.
/// </summary>
public sealed class DelegationActivityTests
{
    #region Private Fields

    private const string TwoLevels = "delegation-two-levels";

    private const string ChildWaits = "delegation-child-waits";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    #endregion

    #region Live And Reopened

    [Fact]
    public void Handed_work_reads_the_same_live_and_after_the_conversation_is_reopened()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{TwoLevels}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{TwoLevels}.messages.json");
        var names = Crew(log, "Scout", "Checker");
        var checker = names.Names.Single(name => name.Value == "Checker").Key;

        var early = Live(log, messages.Take(1), log.First(stored => stored.Type == RunEventTypes.DelegationStarted).Seq + 1, names);
        var working = Card(early.AllTools().Single(tool => tool.Delegation is not null).Delegation!);
        working.State.Should().Be("Working");
        working.Edge.Should().Be("DvViolet", "the edge is violet while the work is out");
        working.Title.Should().Be("Marshal asked Scout: Find three sources on the topic.");
        working.Head.Should().Be(AvatarState.Thinking, "the child's visor breathes while it works");

        var live = Live(log, messages, log[^1].Seq, names);
        var history = ToolHistory.From(ChatBench.Load<List<RunEventResponse>>($"{TwoLevels}.tool-events.json"));
        var saved = ChatTimeline.Merge(messages, null).ToChatMessages(history, handover: names);

        Outline(saved).Should().Equal(Outline(live));
        var outer = Card(saved.AllTools().Single(tool => tool.Delegation is not null).Delegation!, open: true);
        outer.State.Should().Be("Done");
        outer.Edge.Should().Be("DvOk");
        outer.Item.Task.Should().Be("Find three sources on the topic.\nKeep each to one line.");
        var scoutsCalls = outer.Item.Work.AllTools().ToList();
        scoutsCalls.Where(tool => tool.Delegation is null).Select(tool => tool.Label).Should().Equal(["test.lookup"], "Scout's own call, with the same card");
        var inner = Card(scoutsCalls.Single(tool => tool.Delegation is not null).Delegation!, open: true);
        Titles(outer, scoutsCalls, inner).Should().Equal(
            ["What Scout was asked", "What Scout did", "Result", "What Checker was asked", "What Checker answered", "What Scout answered"],
            "a child's last reply is its answer, shown once, whole, at the end");
        inner.Title.Should().Be("Scout asked Checker: Check that the second source holds up.");
        inner.LinkHref.Should().StartWith($"/talk/{botId}/work/{checker}/");
        inner.Answer!.Trim().Should().Be("The second source holds up.");
        outer.Answer.Should().StartWith("Three sources");
        outer.Item.Work.Select(entry => entry.Text).Should().NotContain(text => text.StartsWith("Three sources", StringComparison.Ordinal), "Scout's answer is not repeated in its work");
    }

    #endregion

    #region A Child Asks

    [Fact]
    public async Task A_childs_approval_opens_its_card_names_the_child_and_is_answered_from_the_parents_conversation()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ChildWaits}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{TwoLevels}.messages.json");
        var names = Crew(log, "Scribe", "Unused");
        var asked = RunEventTypes.Read<ToolApprovalRequested>(log.Single(stored => stored.Type == RunEventTypes.ToolApprovalRequested));
        List<(Guid, bool)> answers = [];

        var started = Live(log, messages.Take(1), log.First(stored => stored.Type == RunEventTypes.DelegationStarted).Seq, names);
        var handed = Card(started.AllTools().Single(tool => tool.Delegation is not null).Delegation!);
        handed.Open.Should().BeFalse();
        var conversation = Live(log, messages.Take(1), log[^1].Seq, names);
        handed.Item = conversation.AllTools().Single(tool => tool.Delegation is not null).Delegation!;
        handed.ParametersSet();

        handed.State.Should().Be("Waiting for you");
        handed.Open.Should().BeTrue("the card opens by itself so the buttons are in view");
        handed.Head.Should().NotBe(AvatarState.Thinking, "a child that waits for a person is not working");
        handed.Edge.Should().Be("DvViolet", "the work is still out");
        var ask = new ToolCallCardViewModel
        {
            Card = handed.Item.Work.AllTools().Single(tool => tool.Phase is ToolPhase.WaitingForYou),
            BotName = handed.Item.ChildName,
            Decide = (id, approved) =>
            {
                answers.Add((id, approved));
                return Task.FromResult<string?>(null);
            },
        };
        ask.ParametersSet();
        ask.IsAsk.Should().BeTrue();
        ask.AskTitle.Should().StartWith("Scribe asks to");
        ask.Deciding.Should().BeFalse();
        new ToolAnnouncer().Next(conversation.AllTools("Marshal")).Should().Contain("Scribe asks to");

        await ask.Answer(approved: true);

        answers.Should().Equal((asked.ToolCallId, true));
    }

    [Fact]
    public void A_call_of_a_child_still_at_work_is_not_history()
    {
        var log = ChatBench.Load<List<RunEventResponse>>($"{ChildWaits}.json");
        var messages = ChatBench.Load<List<MessageResponse>>($"{TwoLevels}.messages.json");
        var names = Crew(log, "Scribe", "Unused");
        var child = RunEventTypes.Read<DelegationStarted>(log.Single(stored => stored.Type == RunEventTypes.DelegationStarted)).ChildRunId;
        var requested = log.First(stored => stored.RunId == child && stored.Type == RunEventTypes.ToolRequested).Seq;

        var early = Live(log, messages.Take(1), requested, names);
        var later = Live(log, messages.Take(1), log[^1].Seq, names);

        early.AllTools().Single(tool => tool.Delegation is not null).Delegation!.Work.AllTools().Select(tool => tool.State).Should().Equal("Starting");
        later.AllTools("Marshal").Select(call => call.Card.State).Should().NotContain("Stopped");
    }

    #endregion

    #region Private Methods

    // The conversation while the recorded run is under way: what is stored, then what the run has said so far.
    private static IReadOnlyList<ChatMessage> Live(List<RunEventResponse> log, IEnumerable<MessageResponse> stored, long to, Handover names)
    {
        var run = RunReducer.Apply(RunView.Start(log[0].RunId), log.Where(each => each.Seq <= to));
        return ChatTimeline.Merge(stored, run).ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.With(run), run, handover: names);
    }

    // The bots the recorded runs handed work to, in the order they did, named and placed in the crew.
    private static Handover Crew(List<RunEventResponse> log, string first, string second)
    {
        var started = log.Where(stored => stored.Type == RunEventTypes.DelegationStarted).Select(RunEventTypes.Read<DelegationStarted>).ToList();
        var names = new Dictionary<Guid, string> { [botId] = "Marshal", [started[0].ChildBotId] = first };
        if (started.Count > 1)
        {
            names[started[1].ChildBotId] = second;
        }

        return new("Marshal", names, (bot, thread) => Addresses.Work(botId, bot, thread));
    }

    private static DelegationCardViewModel Card(DelegationItem item, bool open = false)
    {
        var card = new DelegationCardViewModel { Item = item, HasWork = item.Work.Count > 0 };
        card.ParametersSet();
        card.Open = open;
        return card;
    }

    // The headings a person meets reading the outer card from top to bottom with everything in it open.
    private static IEnumerable<string> Titles(DelegationCardViewModel outer, List<ToolCard> calls, DelegationCardViewModel inner)
    {
        var work = calls.SelectMany(tool => tool.Delegation is null ? Opened(tool).PartTitles : inner.PartTitles);
        return [outer.TaskTitle, outer.WorkTitle, .. work, outer.AnswerTitle];
    }

    private static ToolCallCardViewModel Opened(ToolCard tool)
    {
        var card = new ToolCallCardViewModel { Card = tool, BotName = "Scout" };
        card.ParametersSet();
        card.Open = true;
        return card;
    }

    // What the cards of a conversation say, level by level: all a person sees or hears of them.
    private static IEnumerable<string> Outline(IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(message => message.Tools.SelectMany(Outline).Prepend($"said: {message.Text}"));

    private static IEnumerable<string> Outline(ToolCard tool)
    {
        if (tool.Delegation is not { } work)
        {
            var shown = Opened(tool);
            return [$"{tool.Label} | {tool.State} | {tool.Summary} | {string.Join(",", shown.PartTitles)} | {tool.Result}"];
        }

        var card = Card(work, open: true);
        return
        [
            $"{card.Title} | {card.State} | {card.Edge} | {string.Join(",", card.PartTitles)} | {work.Task} | {card.Answer} | {card.LinkHref is not null}",
            .. Outline(work.Work),
        ];
    }

    #endregion
}
