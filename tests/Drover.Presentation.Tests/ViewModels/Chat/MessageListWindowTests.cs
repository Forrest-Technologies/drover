namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A long conversation puts its newest messages on the page and keeps every message: going back, a link to a
/// moment, and sending each move the page, and what is read out and counted still comes from all of it.
/// </summary>
public sealed class MessageListWindowTests
{
    #region Public Methods

    [Fact]
    public void A_long_conversation_shows_its_newest_messages_and_keeps_them_all()
    {
        var list = Long(400);

        list.Messages.Should().HaveCount(400);
        list.Shown.Count.Should().BeLessThan(100);
        list.Shown[^1].Should().BeSameAs(list.Messages[^1]);
        list.Earlier.Should().Be(400 - list.Shown.Count);
        list.Later.Should().Be(0);
    }

    [Fact]
    public void Going_back_shows_earlier_messages_and_says_so_once()
    {
        var list = Long(400);
        var shown = list.Shown.Count;
        var changes = 0;
        list.PropertyChanged += (_, _) => changes++;

        list.ShowEarlier();

        list.Shown.Count.Should().BeGreaterThan(shown);
        list.Shown[^1].Should().BeSameAs(list.Messages[^1]);
        changes.Should().Be(1);
    }

    [Fact]
    public void Going_back_in_a_conversation_shown_whole_says_nothing()
    {
        var list = Long(6);
        var changes = 0;
        list.PropertyChanged += (_, _) => changes++;

        list.ShowEarlier();
        list.ShowLater();

        list.Shown.Should().BeSameAs(list.Messages);
        changes.Should().Be(0);
    }

    [Fact]
    public void A_moment_far_back_is_put_on_the_page_when_it_is_taken()
    {
        var list = Long(2000);
        var wanted = list.Messages[100];
        list.Moment = Guid.Parse(wanted.Key);

        list.TakeMoment().Should().Be(list.Moment);

        list.Shown.Should().Contain(wanted);
        list.Shown.Count.Should().BeLessThan(100, "landing far back does not put everything between on the page");
        list.Later.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_page_that_left_the_newest_does_not_say_the_bot_is_thinking_there()
    {
        var list = Long(2000);
        list.Thinking = true;
        list.ShowsThinking.Should().BeTrue();

        list.Moment = Guid.Parse(list.Messages[100].Key);
        list.TakeMoment();

        list.ShowsThinking.Should().BeFalse("the bot thinks after the newest message, which is not on the page");
    }

    [Fact]
    public void Sending_brings_the_newest_back_from_anywhere()
    {
        var list = Long(2000);
        list.Moment = Guid.Parse(list.Messages[100].Key);
        list.TakeMoment();
        var asked = 0;
        list.NewestRequested += () => asked++;

        list.ShowNewest();

        list.Later.Should().Be(0);
        list.Shown[^1].Should().BeSameAs(list.Messages[^1]);
        asked.Should().Be(1);
    }

    [Fact]
    public void The_page_stays_on_its_messages_while_the_conversation_is_worked_out_anew()
    {
        var list = Long(400);
        list.ShowEarlier();
        string[] keys = [.. list.Shown.Select(message => message.Key)];

        // Each change builds every message again, and a reply gains a message at the end.
        list.Messages = [.. list.Messages.Select(message => message with { }), new(Guid.CreateVersion7().ToString(), false, "A new reply.")];
        list.Update();

        list.Shown.Select(message => message.Key).Should().StartWith(keys);
        list.Shown.Should().HaveCount(keys.Length + 1);
    }

    [Fact]
    public void A_turn_is_not_cut_where_the_page_begins()
    {
        List<ChatMessage> messages = [];
        for (var index = 0; index < 300; index++)
        {
            messages.Add(new(Guid.CreateVersion7().ToString(), index % 3 == 0, $"Message {index}") { ContinuesTurn = index % 3 == 2 });
        }

        var list = new MessageListViewModel { BotName = "Sample bot", Messages = messages };

        for (var step = 0; step < 5; step++)
        {
            list.Shown[0].ContinuesTurn.Should().BeFalse();
            list.ShowEarlier();
        }
    }

    [Fact]
    public void What_is_read_out_and_which_reply_is_newest_come_from_the_whole_conversation()
    {
        var list = Long(2000);
        list.Moment = Guid.Parse(list.Messages[100].Key);
        list.TakeMoment();

        list.IsNewestReply(list.Messages[^1]).Should().BeTrue();
        list.IsNewestReply(list.Shown[^1]).Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static MessageListViewModel Long(int count)
    {
        List<ChatMessage> messages = [];
        for (var index = 0; index < count; index++)
        {
            messages.Add(new(Guid.CreateVersion7().ToString(), index % 2 == 0, $"Message {index}"));
        }

        var list = new MessageListViewModel { BotName = "Sample bot", Messages = messages };
        list.Update();
        return list;
    }

    #endregion
}
