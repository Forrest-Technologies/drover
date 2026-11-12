namespace Drover.Ui.Tests.Chat;

public sealed class SentenceAnnouncerTests
{
    #region Private Fields

    private readonly SentenceAnnouncer announcer = new();

    #endregion

    #region Public Methods

    [Fact]
    public void Nothing_is_said_until_a_sentence_is_complete()
    {
        announcer.Next("a", "The build", streaming: true).Should().BeNull();
        announcer.Next("a", "The build passed.", streaming: true).Should().BeNull("the mark may still be mid-number");
        announcer.Next("a", "The build passed. Next", streaming: true).Should().Be("The build passed.");
    }

    [Fact]
    public void Each_sentence_is_said_once()
    {
        announcer.Next("a", "One. Two", streaming: true).Should().Be("One.");
        announcer.Next("a", "One. Two", streaming: true).Should().BeNull();
        announcer.Next("a", "One. Two? Three", streaming: true).Should().Be("Two?");
    }

    [Fact]
    public void A_number_with_a_point_is_not_cut_in_two()
    {
        announcer.Next("a", "It costs 3.", streaming: true).Should().BeNull();
        announcer.Next("a", "It costs 3.14 in all. Done", streaming: true).Should().Be("It costs 3.14 in all.");
    }

    [Fact]
    public void A_line_break_ends_a_sentence()
    {
        announcer.Next("a", "First item\nSecond", streaming: true).Should().Be("First item");
    }

    [Fact]
    public void The_rest_is_said_when_the_reply_ends()
    {
        announcer.Next("a", "One. Two", streaming: true);

        announcer.Next("a", "One. Two and done", streaming: false).Should().Be("Two and done");
        announcer.Next("a", "One. Two and done", streaming: false).Should().BeNull();
    }

    [Fact]
    public void A_new_message_starts_over()
    {
        announcer.Next("a", "One. Two", streaming: true);

        announcer.Next("b", "Other. More", streaming: true).Should().Be("Other.");
    }

    #endregion
}
