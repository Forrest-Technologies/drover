namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The one line under a conversation's composer (docs/ux/conversation-display.md, section 1): while a run works, the
/// state and the model; at rest, the model that wrote the newest reply, its tokens and its cost. Parts are joined by a
/// middle dot, a part that is not known is left out, and what the newest reply used is read once.
/// </summary>
public sealed class ConversationStatusTests
{
    #region Private Fields

    private const string Model = "vendor/sample-model";

    private readonly Guid run = Guid.CreateVersion7();

    private readonly List<Guid> asked = [];

    #endregion

    #region Nothing Said Yet

    [Fact]
    public void A_new_conversation_shows_its_model_as_plain_text_and_asks_the_server_nothing()
    {
        var status = Shown(new([], Model: Model));

        status.Line.Should().Be(Model);
        status.Usage.Should().BeNull("with nothing to open the line is plain text");
        status.Name.Should().Be("vendor/sample-model.");
        asked.Should().BeEmpty();
    }

    [Fact]
    public void With_no_model_known_and_nothing_said_the_line_is_not_there()
    {
        var status = Shown(new([new("q", true, "Hello")]));

        status.HasAny.Should().BeFalse("a line with nothing to say takes no room");
        status.Line.Should().BeEmpty();
        status.Name.Should().BeEmpty();
    }

    [Fact]
    public void A_model_chosen_for_this_conversation_only_says_so()
    {
        Shown(new([], Model: "vendor/sample-stronger", ModelIsChosen: true))
            .Line.Should().Be("vendor/sample-stronger, for this conversation");
    }

    #endregion

    #region While A Run Works

    [Fact]
    public void While_a_run_works_the_line_is_the_state_then_the_model()
    {
        var status = Shown(new([new("q", true, "Hello")], Working: true, Thinking: true, Model: Model));

        status.Line.Should().Be("Thinking · vendor/sample-model");
        status.Parts[0].IsState.Should().BeTrue("the state is the one live part of the line");
        status.Parts[1].IsState.Should().BeFalse();
        status.Usage.Should().BeNull();
    }

    [Fact]
    public void The_state_is_said_in_the_words_the_conversation_already_uses()
    {
        var reasoning = new ChatMessage("a", false, string.Empty, Streaming: true) { Reasoning = "Weighing it" };
        var writing = new ChatMessage("a", false, "Here is", Streaming: true);
        var tool = new ToolCard(Guid.CreateVersion7(), ToolPhase.Running, "Running a command", "run a command", "ls", "{}", true, null, null, []);
        var calling = new ChatMessage("a", false, "Let me look.") { Tools = [tool] };

        ConversationStatusViewModel.State(new([reasoning], Working: true)).Should().Be(ChatRows.Arriving(reasoning)).And.Be("Thinking");
        ConversationStatusViewModel.State(new([writing], Working: true)).Should().Be(ChatRows.Arriving(writing)).And.Be("Writing");
        ConversationStatusViewModel.State(new([calling], Working: true)).Should().Be("Running a command");
        ConversationStatusViewModel.State(new([calling], Working: true, Waiting: true)).Should().Be("Waiting for you");
        ConversationStatusViewModel.State(new([], Working: true)).Should().BeNull("a state that is not known is left out");
    }

    [Fact]
    public void A_run_under_way_shows_no_tokens_and_no_cost_and_opens_nothing()
    {
        ChatMessage[] first = [new("q1", true, "First"), Finished("a1", run, Model)];
        var status = Shown(new(first));

        status.Show(new([.. first, new("q2", true, "Second")], Working: true, Thinking: true, Model: Model), Read(), NullLogger.Instance);

        status.Line.Should().Be("Thinking · vendor/sample-model");
        status.Usage.Should().BeNull();

        status.Show(new([.. first, new("q2", true, "Second")]), Read(), NullLogger.Instance);

        status.Line.Should().Be("vendor/sample-model · 1,240 tokens · USD 0.02", "the run ended with no reply, so the newest reply is the one before");
        asked.Should().Equal([run], "what was read for that reply is kept while the run worked");
    }

    #endregion

    #region The Newest Finished Reply

    [Fact]
    public void At_rest_the_line_is_the_model_the_tokens_and_the_cost_and_it_opens_the_usage()
    {
        ChatMessage[] conversation = [new("q", true, "Hello"), Finished("a", run, Model)];
        var status = Shown(new(conversation, Model: "vendor/the-bots-own"));

        status.Show(new(conversation), Read(), NullLogger.Instance);

        status.Line.Should().Be("vendor/sample-model · 1,240 tokens · USD 0.02");
        status.Parts.Select(part => part.IsNumber).Should().Equal(false, true, true);
        status.Parts.Should().NotContain(part => part.IsState);
        status.Usage.Should().NotBeNull();
        status.Name.Should().Be("vendor/sample-model, 1,240 tokens, USD 0.02. Shows usage for this reply.");
        asked.Should().Equal([run], "one request for a finished run, however often the conversation is drawn");
    }

    [Fact]
    public void A_price_that_is_not_known_is_left_out_and_the_bots_asked_are_counted()
    {
        var status = new ConversationStatusViewModel();
        var detail = new RunUsageDetail(run, "USD", new UsageFigure(null, 10, 5), new UsageFigure(null, 400, 100));

        status.Show(new([Finished("a", run, null)]), _ => Task.FromResult(detail), NullLogger.Instance);

        status.Line.Should().Be("500 tokens", "neither a model nor a price that is not known is shown as nothing");
    }

    [Fact]
    public void Usage_still_being_read_leaves_the_figures_out_until_they_arrive()
    {
        var status = new ConversationStatusViewModel();
        var waiting = new TaskCompletionSource<RunUsageDetail>();
        var changes = 0;
        status.PropertyChanged += (_, _) => changes++;

        status.Show(new([Finished("a", run, Model)]), _ => waiting.Task, NullLogger.Instance);

        status.Line.Should().Be(Model);
        status.Usage.Should().NotBeNull("the line already opens the usage, which says it is being added up");

        var before = changes;
        waiting.SetResult(new(run, "USD", new UsageFigure(0.02m, 1_040, 200), null));

        status.Line.Should().Be("vendor/sample-model · 1,240 tokens · USD 0.02");
        changes.Should().BeGreaterThan(before, "the screen is told the figures arrived");
    }

    [Fact]
    public void A_reply_that_arrives_again_with_its_model_keeps_the_read_that_was_started_for_it()
    {
        var status = new ConversationStatusViewModel();
        var waiting = new TaskCompletionSource<RunUsageDetail>();
        Func<Guid, Task<RunUsageDetail>> read = runId =>
        {
            asked.Add(runId);
            return waiting.Task;
        };

        // The run ends: the live message has no model yet, and the read starts.
        status.Show(new([Finished("live", run, null)]), read, NullLogger.Instance);
        var first = status.Usage;

        // The stored message arrives with the model that wrote it, before the read has answered.
        status.Show(new([Finished("stored", run, Model)]), read, NullLogger.Instance);
        status.Usage.Should().BeSameAs(first, "one view model for a run, so the read lands where the line looks");
        status.Line.Should().Be(Model);

        waiting.SetResult(new(run, "USD", new UsageFigure(0.02m, 1_040, 200), null));

        status.Line.Should().Be("vendor/sample-model · 1,240 tokens · USD 0.02");
        status.Usage!.WrittenBy.Should().Be("Written by vendor/sample-model");
        asked.Should().Equal([run]);
    }

    [Fact]
    public void Usage_that_could_not_be_read_leaves_the_figures_out_and_the_popover_says_what_failed()
    {
        var status = new ConversationStatusViewModel();

        status.Show(
            new([Finished("a", run, Model)]),
            _ => Task.FromException<RunUsageDetail>(new HttpRequestException("unreachable")),
            NullLogger.Instance);

        status.Line.Should().Be(Model);
        status.Usage!.Problem.Should().StartWith("Drover could not load usage. ");
    }

    [Fact]
    public void A_person_who_may_not_read_usage_gets_the_model_as_plain_text()
    {
        var theirs = new ChatMessage("a", false, "Done.") { RunId = run, FinishesReply = true, Model = Model };

        var status = Shown(new([theirs]));

        status.Line.Should().Be(Model);
        status.Usage.Should().BeNull();
        status.Name.Should().Be("vendor/sample-model.");

        var none = new ConversationStatusViewModel();
        none.Show(new([Finished("a", run, Model)]), load: null, NullLogger.Instance);
        none.Usage.Should().BeNull("a screen with no way to read usage opens none");
        asked.Should().BeEmpty();
    }

    [Fact]
    public void Only_the_newest_reply_is_read_and_a_new_one_is_read_when_it_finishes()
    {
        var second = Guid.CreateVersion7();
        ChatMessage[] first = [new("q1", true, "First"), Finished("a1", run, Model)];
        var status = Shown(new(first));

        ChatMessage[] done = [.. first, new("q2", true, "Second"), Finished("a2", second, "vendor/sample-stronger")];
        status.Show(new(done), Read(), NullLogger.Instance);

        status.Line.Should().Be("vendor/sample-stronger · 1,240 tokens · USD 0.02");
        asked.Should().Equal([run, second], "one request for each finished run, none for an older reply");
    }

    [Fact]
    public void The_conversations_list_keeps_the_line_and_tells_it_what_the_conversation_holds()
    {
        var list = new MessageListViewModel
        {
            BotName = "Marshal",
            Messages = [new("q", true, "Hello")],
            Working = true,
            Thinking = true,
            Model = Model,
            LoadUsage = Read(),
        };

        list.Update();
        list.Status.Line.Should().Be("Thinking · vendor/sample-model");

        list.Messages = [new("q", true, "Hello"), Finished("a", run, Model)];
        (list.Working, list.Thinking) = (false, false);
        list.Update();

        list.Status.Line.Should().Be("vendor/sample-model · 1,240 tokens · USD 0.02");
    }

    #endregion

    #region Private Methods

    private ConversationStatusViewModel Shown(ConversationStatusFacts facts)
    {
        var status = new ConversationStatusViewModel();
        status.Show(facts, Read(), NullLogger.Instance);
        return status;
    }

    private static ChatMessage Finished(string key, Guid runId, string? model) =>
        new(key, false, "Here is the plan.") { RunId = runId, EndsReply = true, FinishesReply = true, Model = model };

    private Func<Guid, Task<RunUsageDetail>> Read() => runId =>
    {
        asked.Add(runId);
        return Task.FromResult(new RunUsageDetail(runId, "USD", new UsageFigure(0.02m, 1_040, 200), null));
    };

    #endregion
}
