namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The one line under a conversation's composer: what the bot is doing now in words, and the newest finished reply's
/// model and figures, read once for each finished run. With nothing to say the line is not there.
/// </summary>
public sealed class ConversationStatusTests
{
    #region Private Fields

    private readonly Guid run = Guid.CreateVersion7();

    private readonly List<Guid> asked = [];

    #endregion

    #region At Rest

    [Fact]
    public void At_rest_with_no_reply_the_line_says_nothing_and_asks_the_server_nothing()
    {
        var status = new ConversationStatusViewModel();

        status.Show([new("q", true, "Hello")], thinking: false, Read(), NullLogger.Instance);

        status.State.Should().BeNull();
        status.Usage.Should().BeNull();
        status.HasAny.Should().BeFalse("a line with nothing to say takes no room");
        asked.Should().BeEmpty();
    }

    #endregion

    #region Live State

    [Fact]
    public void While_the_bot_thinks_before_any_words_the_line_says_thinking()
    {
        var status = new ConversationStatusViewModel();

        status.Show([new("q", true, "Hello")], thinking: true, Read(), NullLogger.Instance);

        status.State.Should().Be("Thinking");
        status.Usage.Should().BeNull();
        status.HasAny.Should().BeTrue();
    }

    [Fact]
    public void While_a_reply_arrives_the_line_says_what_the_reply_itself_says()
    {
        var reasoning = new ChatMessage("a", false, string.Empty, Streaming: true) { Reasoning = "Weighing it" };
        var writing = new ChatMessage("a", false, "Here is", Streaming: true);

        ChatRows.LiveState([reasoning], thinking: false).Should().Be(ChatRows.Arriving(reasoning)).And.Be("Thinking");
        ChatRows.LiveState([writing], thinking: false).Should().Be(ChatRows.Arriving(writing)).And.Be("Writing");
        ChatRows.LiveState([writing with { Streaming = false }], thinking: false).Should().BeNull("nothing is said at rest");
    }

    #endregion

    #region The Newest Finished Reply

    [Fact]
    public void A_finished_reply_shows_its_model_and_reads_what_it_used_once()
    {
        var status = new ConversationStatusViewModel();
        ChatMessage[] conversation = [new("q", true, "Hello"), Finished("a", run, "vendor/sample-model")];

        status.Show(conversation, thinking: false, Read(), NullLogger.Instance);
        status.Show(conversation, thinking: false, Read(), NullLogger.Instance);

        status.State.Should().BeNull();
        status.Usage!.ShortModel.Should().Be("sample-model");
        status.Usage.Figures.Should().Be("1.2k tokens, USD 0.02");
        status.Usage.HintName.Should().Be("Written by vendor/sample-model, 1,200 tokens, USD 0.02. Show what this reply used.");
        asked.Should().Equal([run], "one request for a finished run, however often the conversation is drawn");
    }

    [Fact]
    public void A_finished_reply_whose_figures_are_not_read_yet_shows_its_model_only()
    {
        var status = new ConversationStatusViewModel();
        var waiting = new TaskCompletionSource<RunUsageDetail>();

        status.Show([Finished("a", run, "vendor/sample-model")], thinking: false, _ => waiting.Task, NullLogger.Instance);

        status.Usage!.ShortModel.Should().Be("sample-model");
        status.Usage.Figures.Should().BeNull("a figure is shown once it is read, never as nothing in its place");
        status.HasAny.Should().BeTrue();
    }

    [Fact]
    public void Figures_that_could_not_be_read_are_left_out_and_the_popover_says_what_failed()
    {
        var status = new ConversationStatusViewModel();

        status.Show(
            [Finished("a", run, "vendor/sample-model")],
            thinking: false,
            _ => Task.FromException<RunUsageDetail>(new HttpRequestException("unreachable")),
            NullLogger.Instance);

        status.Usage!.ShortModel.Should().Be("sample-model");
        status.Usage.Figures.Should().BeNull();
        status.Usage.Problem.Should().NotBeNullOrWhiteSpace("the popover the line opens says what failed");
    }

    [Fact]
    public void A_reply_this_person_may_not_read_the_usage_of_shows_nothing()
    {
        var status = new ConversationStatusViewModel();
        var theirs = new ChatMessage("a", false, "Done.") { RunId = run, FinishesReply = true, Model = "vendor/sample-model" };

        status.Show([theirs], thinking: false, Read(), NullLogger.Instance);
        status.Usage.Should().BeNull();

        status.Show([Finished("a", run, "vendor/sample-model")], thinking: false, load: null, NullLogger.Instance);
        status.Usage.Should().BeNull("a screen with no way to read usage shows none");
        asked.Should().BeEmpty();
    }

    [Fact]
    public void Only_the_newest_reply_is_read_and_a_new_one_is_read_when_it_finishes()
    {
        var status = new ConversationStatusViewModel();
        var second = Guid.CreateVersion7();
        ChatMessage[] first = [new("q1", true, "First"), Finished("a1", run, "vendor/sample-model")];

        status.Show(first, thinking: false, Read(), NullLogger.Instance);

        ChatMessage[] arriving = [.. first, new("q2", true, "Second"), new("a2", false, "Here is", Streaming: true) { RunId = second }];
        status.Show(arriving, thinking: false, Read(), NullLogger.Instance);

        status.State.Should().Be("Writing");
        status.Usage.Should().BeNull("the newest reply has not finished, and the one before it is an older reply now");

        ChatMessage[] done = [.. first, new("q2", true, "Second"), Finished("a2", second, "vendor/sample-stronger")];
        status.Show(done, thinking: false, Read(), NullLogger.Instance);

        status.State.Should().BeNull();
        status.Usage!.ShortModel.Should().Be("sample-stronger");
        asked.Should().Equal([run, second], "one request for each finished run, none for an older reply");
    }

    [Fact]
    public void The_list_keeps_its_status_line_and_the_newest_replys_footer_leaves_the_hint_to_it()
    {
        var reply = Finished("a", run, "vendor/sample-model");
        var list = new MessageListViewModel { BotName = "Marshal", Messages = [new("q", true, "Hello"), reply], LoadUsage = Read() };

        list.Update();

        list.Status.Usage!.Figures.Should().Be("1.2k tokens, USD 0.02");
        ChatRows.FooterParts(reply, canRegenerate: false, canSwitch: false, canReadUsage: true, list.IsNewestReply(reply))
            .Should().Equal(FooterPart.Copy);
    }

    #endregion

    #region Private Methods

    private static ChatMessage Finished(string key, Guid runId, string model) =>
        new(key, false, "Here is the plan.") { RunId = runId, EndsReply = true, FinishesReply = true, Model = model };

    private Func<Guid, Task<RunUsageDetail>> Read() => runId =>
    {
        asked.Add(runId);
        return Task.FromResult(new RunUsageDetail(runId, "USD", new UsageFigure(0.02m, 1_000, 200), null));
    };

    #endregion
}
