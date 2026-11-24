namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The quiet line of actions under a finished reply: which replies offer to copy their words, the usage of the reply,
/// another reply and their versions, in what order, and which replies keep them out of sight until asked.
/// </summary>
public sealed class ReplyFooterTests
{
    #region Usage

    [Fact]
    public async Task A_replys_usage_is_read_when_asked_for_and_titled_as_its_action_is_named()
    {
        var detail = new RunUsageDetail(Guid.CreateVersion7(), "USD", new UsageFigure(0.02m, 1_000, 200), null);
        var asked = 0;
        var usage = new ReplyUsageViewModel(detail.RunId, _ => Task.FromResult(Count(detail)), NullLogger.Instance, "vendor/sample-stronger");

        ReplyUsageViewModel.Label.Should().Be("Usage for this reply");
        usage.WrittenBy.Should().Be("Written by vendor/sample-stronger");
        asked.Should().Be(0, "nothing is read until a person asks");

        await usage.Toggle();

        usage.Lines.Should().NotBeEmpty();
        asked.Should().Be(1);

        usage.Model = "vendor/another";
        usage.WrittenBy.Should().Be("Written by vendor/another", "the model can become known after the view model was made");
        usage.Detail.Should().BeSameAs(detail, "what was read stays");

        RunUsageDetail Count(RunUsageDetail read)
        {
            asked++;
            return read;
        }
    }

    [Fact]
    public async Task Usage_that_could_not_be_read_says_so_in_the_words_of_the_design()
    {
        var usage = new ReplyUsageViewModel(
            Guid.CreateVersion7(),
            _ => Task.FromException<RunUsageDetail>(new HttpRequestException("unreachable")),
            NullLogger.Instance);

        await usage.Toggle();

        usage.Problem.Should().StartWith("Drover could not load usage. ");
        usage.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Every_reply_whose_usage_may_be_read_offers_it_the_newest_too()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.") { RunId = Guid.CreateVersion7(), FinishesReply = true, EndsReply = true };

        ChatRows.FooterParts(reply, canRegenerate: false, canSwitch: false, canReadUsage: true)
            .Should().Equal(FooterPart.Copy, FooterPart.Usage);
        ChatRows.FooterParts(reply, canRegenerate: false, canSwitch: false, canReadUsage: false)
            .Should().Equal(FooterPart.Copy);
    }

    #endregion

    #region Regenerate

    [Fact]
    public void Another_reply_is_offered_only_where_the_reply_may_be_written_again()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.") { EndsReply = true, MayRegenerate = true };

        ChatRows.OffersRegenerate(reply, canAsk: true).Should().BeTrue();
        ChatRows.OffersRegenerate(reply with { MayRegenerate = false }, canAsk: true).Should().BeFalse();
        ChatRows.OffersRegenerate(reply, canAsk: false).Should().BeFalse("a screen without a way to ask offers nothing");
        ChatRows.OffersRegenerate(reply with { Streaming = true }, canAsk: true).Should().BeFalse("nothing stands under words still arriving");
        ChatRows.OffersRegenerate(new ChatMessage("b", true, "Hello") { MayRegenerate = true }, canAsk: true).Should().BeFalse();
    }

    #endregion

    #region Order

    [Fact]
    public void The_footer_stands_copy_then_the_usage_then_another_reply_then_the_versions()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.")
        {
            RunId = Guid.CreateVersion7(),
            FinishesReply = true,
            EndsReply = true,
            MayRegenerate = true,
            Versions = new VersionSwitch(2, 3, Guid.CreateVersion7(), null),
        };

        ChatRows.FooterParts(reply, canRegenerate: true, canSwitch: true, canReadUsage: true)
            .Should().Equal(FooterPart.Copy, FooterPart.Usage, FooterPart.Regenerate, FooterPart.Versions);
        Enum.GetValues<FooterPart>().Should().Equal(
            [FooterPart.Copy, FooterPart.Usage, FooterPart.Regenerate, FooterPart.Versions],
            "the line places its parts in this order");
    }

    [Fact]
    public void The_footer_holds_only_what_the_reply_and_the_screen_offer()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.") { RunId = Guid.CreateVersion7(), FinishesReply = true };

        ChatRows.FooterParts(reply, canRegenerate: true, canSwitch: true, canReadUsage: true).Should().Equal(FooterPart.Copy);
        ChatRows.FooterParts(reply with { Streaming = true }, canRegenerate: true, canSwitch: true, canReadUsage: true).Should().BeEmpty();
    }

    #endregion

    #region The Newest Reply

    [Fact]
    public void The_newest_reply_is_the_bots_last_messages_back_to_what_a_person_said()
    {
        ChatMessage[] conversation =
        [
            new("q1", true, "First"),
            new("a1", false, "An older answer."),
            new("q2", true, "Second"),
            new("a2", false, "The answer begins."),
            new("a3", false, "And goes on.") { ContinuesTurn = true },
        ];

        ChatRows.NewestReply(conversation).Should().BeEquivalentTo(["a2", "a3"]);
        ChatRows.NewestReply([]).Should().BeEmpty();
        ChatRows.NewestReply([new("q1", true, "First")]).Should().BeEmpty("nothing has been answered yet");
    }

    [Fact]
    public void A_message_sent_after_it_does_not_make_the_newest_reply_an_older_one()
    {
        ChatMessage[] conversation =
        [
            new("q1", true, "First"),
            new("a1", false, "The answer."),
            new("q2", true, "Second, not answered yet"),
        ];

        ChatRows.NewestReply(conversation).Should().BeEquivalentTo(["a1"]);
    }

    [Fact]
    public void The_list_says_which_messages_are_of_the_newest_reply_and_follows_what_it_holds()
    {
        var older = new ChatMessage("a1", false, "An older answer.");
        var newest = new ChatMessage("a2", false, "The newest answer.");
        var list = new MessageListViewModel { Messages = [new("q1", true, "First"), older, new("q2", true, "Second"), newest] };

        list.IsNewestReply(newest).Should().BeTrue();
        list.IsNewestReply(older).Should().BeFalse();

        var later = new ChatMessage("a3", false, "A later answer.");
        list.Messages = [.. list.Messages, new("q3", true, "Third"), later];

        list.IsNewestReply(later).Should().BeTrue();
        list.IsNewestReply(newest).Should().BeFalse("a reply that came after it made it an older one");
    }

    [Fact]
    public void The_newest_reply_shows_its_actions_and_an_older_one_only_while_asked()
    {
        ChatRows.ShowsActions(newest: true, revealed: false).Should().BeTrue();
        ChatRows.ShowsActions(newest: false, revealed: false).Should().BeFalse("nothing stands under an older reply at rest");
        ChatRows.ShowsActions(newest: false, revealed: true).Should().BeTrue();
    }

    [Fact]
    public void Only_an_older_finished_reply_with_something_to_offer_is_a_stop_that_reveals_its_actions()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.") { FinishesReply = true };

        ChatRows.RevealsActions(reply, newest: false, hasFooter: true, leadsToRun: false).Should().BeTrue();
        ChatRows.RevealsActions(reply, newest: false, hasFooter: false, leadsToRun: true).Should().BeTrue("the way to what happened is an action too");
        ChatRows.RevealsActions(reply, newest: true, hasFooter: true, leadsToRun: true).Should().BeFalse("the newest reply's actions are already in view");
        ChatRows.RevealsActions(reply, newest: false, hasFooter: false, leadsToRun: false).Should().BeFalse("a message with nothing to offer is no stop");
        ChatRows.RevealsActions(reply with { Streaming = true }, newest: false, hasFooter: true, leadsToRun: false).Should().BeFalse();
        ChatRows.RevealsActions(new ChatMessage("b", true, "Hello"), newest: false, hasFooter: true, leadsToRun: false)
            .Should().BeFalse("a person's message is not made a stop");
    }

    #endregion

    #region Copy

    [Fact]
    public void A_finished_reply_written_for_another_member_still_offers_copy_but_not_what_it_used()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var asked = new MessageView(Guid.CreateVersion7(), "user", [new PartView("text", Text: "Plan the week")], false, null);
        var answered = new MessageView(Guid.CreateVersion7(), "assistant", [new PartView("text", Text: "Here is the plan.")], false, null)
        {
            RunId = Guid.CreateVersion7(),
            RunPrincipalUserId = theirs,
        };

        MessageView[] conversation = [asked, answered];

        var shown = conversation.ToChatMessages(reader: new UsageReader(mine, IsSpaceAdmin: false));

        var reply = shown.Single(message => !message.FromPerson);
        reply.FinishesReply.Should().BeTrue();
        reply.EndsReply.Should().BeFalse("what another member's reply used is theirs to read");
        ChatRows.OffersCopy(reply).Should().BeTrue("copy stands under every finished reply");
        ChatRows.FooterParts(reply, canRegenerate: false, canSwitch: false, canReadUsage: true).Should().Equal(FooterPart.Copy);
    }

    [Fact]
    public void A_finished_reply_offers_copy_even_where_no_one_reads_what_replies_used()
    {
        var answered = new MessageView(Guid.CreateVersion7(), "assistant", [new PartView("text", Text: "Done.")], false, null) { RunId = Guid.CreateVersion7() };

        MessageView[] conversation = [answered];

        var reply = conversation.ToChatMessages().Single();

        reply.FinishesReply.Should().BeTrue();
        ChatRows.OffersCopy(reply).Should().BeTrue();
    }

    [Fact]
    public void Only_a_finished_reply_where_the_footer_stands_offers_to_copy_its_words()
    {
        var ended = new ChatMessage("a", false, "Here is the plan.") { EndsReply = true };

        ChatRows.OffersCopy(ended).Should().BeTrue();
        ChatRows.OffersCopy(ended with { Streaming = true }).Should().BeFalse("words still arriving are not offered");
        ChatRows.OffersCopy(ended with { EndsReply = false }).Should().BeFalse("words between steps of a run have no footer");
        ChatRows.OffersCopy(ended with { EndsReply = false, FinishesReply = true }).Should().BeTrue("a reply finished for someone else is still copied");
        ChatRows.OffersCopy(ended with { EndsReply = false, MayRegenerate = true }).Should().BeTrue();
        ChatRows.OffersCopy(ended with { Text = string.Empty }).Should().BeFalse();
        ChatRows.OffersCopy(new ChatMessage("b", true, "Hello") { EndsReply = true }).Should().BeFalse("a person's message is not a reply");
    }

    #endregion
}
