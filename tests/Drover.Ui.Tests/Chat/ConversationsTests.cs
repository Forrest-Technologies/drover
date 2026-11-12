namespace Drover.Ui.Tests.Chat;

public sealed class ConversationsTests
{
    #region Public Methods

    [Fact]
    public void The_conversation_shows_what_the_person_wrote_and_what_the_bot_said()
    {
        MessageView[] timeline =
        [
            Message("user", false, new PartView("text", "What time is it")),
            Message("assistant", false, new PartView("tool_call", ToolName: "time.now", Arguments: "{}")),
            Message("tool", false, new PartView("tool_result", Result: "\"12:00\"")),
            Message("assistant", false, new PartView("reasoning", "Hidden"), new PartView("text", "Noon.")),
        ];

        var shown = timeline.ToChatMessages();

        shown.Select(message => (message.FromPerson, message.Text, message.Streaming))
            .Should().Equal((true, "What time is it", false), (false, "Noon.", false));
        shown.Select(message => message.Key).Should().Equal(timeline[0].Id.ToString(), timeline[3].Id.ToString());
    }

    [Fact]
    public void A_reply_still_arriving_is_shown_as_streaming_once_it_has_words()
    {
        var empty = Message("assistant", true);
        var arriving = Message("assistant", true, new PartView("text", "Half a sent"));

        new[] { empty }.ToChatMessages().Should().BeEmpty();
        new[] { arriving }.ToChatMessages().Single().Should().Be(new ChatMessage(arriving.Id.ToString(), false, "Half a sent", true));
    }

    [Fact]
    public void A_message_the_bot_has_not_taken_yet_is_shown_as_pending()
    {
        var waiting = Message("user", false, new PartView("text", "One more thing")) with { IsPending = true };

        new[] { waiting }.ToChatMessages().Single().Pending.Should().BeTrue();
    }

    [Fact]
    public void A_bot_is_thinking_while_it_works_and_has_said_nothing()
    {
        var working = RunView.Start(Guid.CreateVersion7()) with { Status = "running" };

        working.IsThinking().Should().BeTrue();
        (working with { Messages = [Message("assistant", true)] }).IsThinking().Should().BeTrue();
        (working with { Messages = [Message("assistant", true, new PartView("text", "Words"))] }).IsThinking().Should().BeFalse();
        (working with { Status = "completed" }).IsThinking().Should().BeFalse();
    }

    [Fact]
    public void A_failed_run_says_why_in_the_servers_words_and_what_to_do()
    {
        var failed = RunView.Start(Guid.CreateVersion7()) with { Status = "failed", Error = new("unauthorized", "The provider rejected the API key.") };

        failed.Problem().Should().Be("The provider rejected the API key. Send your message again to retry.");
        failed.Notice().Should().BeNull();
    }

    [Fact]
    public void A_stopped_run_is_a_notice_not_a_failure()
    {
        var stopped = RunView.Start(Guid.CreateVersion7()) with { Status = "cancelled" };

        stopped.Problem().Should().BeNull();
        stopped.Notice().Should().Be("You stopped this reply.");
        (stopped with { Status = "completed" }).Notice().Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static MessageView Message(string role, bool streaming, params PartView[] parts) =>
        new(Guid.CreateVersion7(), role, [.. parts], streaming, null);

    #endregion
}
