namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A reply whose reasoning is arriving speaks from the bot's head and says it is thinking, so the list does not also
/// show the bot thinking below it. Once its words begin it says it is writing. A source is read out with the site it
/// opens, so a title cannot hide where the link goes.
/// </summary>
public sealed class ReplyReasoningTests
{
    #region Tests

    [Fact]
    public void A_reply_whose_reasoning_arrives_says_it_is_thinking_from_the_bots_head()
    {
        var thinking = new ChatMessage("reply", false, string.Empty, Streaming: true) { Reasoning = "A sample thought" };
        var writing = thinking with { Text = "A sample answer" };

        ChatRows.Shape(thinking, compact: false).Should().Be(ChatEntryShape.Bot);
        ChatRows.Arriving(thinking).Should().Be("Thinking");
        ChatRows.Arriving(writing).Should().Be("Writing");
        ChatRows.EntryName(thinking, "Sample bot", null).Should().Be("Sample bot's reasoning");
    }

    [Fact]
    public void A_reply_without_words_or_reasoning_stays_in_line_as_what_the_bot_did()
    {
        var calls = new ChatMessage("calls", false, string.Empty);

        ChatRows.Shape(calls, compact: false).Should().Be(ChatEntryShape.Tools);
        ChatRows.EntryName(calls, "Sample bot", null).Should().Be("Sample bot's activity");
    }

    [Fact]
    public void The_list_shows_the_bot_thinking_only_where_no_reply_already_says_so()
    {
        var list = new MessageListViewModel { Thinking = true, BotName = "Sample bot" };
        list.ShowsThinking.Should().BeTrue();

        list.Messages = [new ChatMessage("reply", false, string.Empty, Streaming: true) { Reasoning = "A sample thought" }];
        list.ShowsThinking.Should().BeFalse();

        list.Messages = [new ChatMessage("reply", false, "A sample answer", Streaming: true)];
        list.ShowsThinking.Should().BeTrue();
    }

    [Fact]
    public void A_source_is_read_out_with_the_site_it_opens()
    {
        var source = SourceLink.From("https://www.sample.example/page", "Sample page")!;

        ChatRows.SourceName(source).Should().Be("Sample page, sample.example, opens outside the app");
        ChatRows.SourceName(SourceLink.From("https://sample.example/", null)!).Should().Be("sample.example, opens outside the app");
    }

    [Fact]
    public void A_source_title_from_the_provider_is_kept_to_one_short_line()
    {
        var source = SourceLink.From("https://sample.example/", $"  A\nsample   {new string('x', 300)}")!;

        source.Title.Should().StartWith("A sample x").And.HaveLength(160).And.EndWith("…");
        SourceLink.From("mailto:someone@sample.example", "Sample").Should().BeNull();
    }

    #endregion
}
