namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The quiet line under a finished reply: the model by a short name, the figures once read, what the hint is called
/// for someone who cannot see it, and which replies offer to copy their words.
/// </summary>
public sealed class ReplyFooterTests
{
    #region Short Names

    [Theory]
    [InlineData("sample-stronger", "sample-stronger")]
    [InlineData("vendor/family/sample-model", "sample-model")]
    [InlineData("deepseek-v4-1-flash-1017-uncensored", "deepseek-v4-1-flash-101…")]
    [InlineData("vendor/a-very-long-sample-model-name-that-goes-on", "a-very-long-sample-mode…")]
    [InlineData("vendor/sample-model/", "sample-model")]
    public void A_model_is_named_short_enough_for_one_line(string model, string shortName)
    {
        var shown = ReplyUsageViewModel.ShortName(model);

        shown.Should().Be(shortName);
        shown.Length.Should().BeLessThanOrEqualTo(24);
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(950L, "950")]
    [InlineData(1_200L, "1.2k")]
    [InlineData(9_999L, "10k")]
    [InlineData(34_567L, "34k")]
    [InlineData(1_500_000L, "1.5M")]
    public void A_count_of_tokens_is_said_in_a_few_characters(long tokens, string shown) =>
        ReplyUsageViewModel.ShortTokens(tokens).Should().Be(shown);

    #endregion

    #region The Hint

    [Fact]
    public async Task The_hint_shows_the_model_then_the_figures_once_read_and_names_all_of_it()
    {
        var detail = new RunUsageDetail(Guid.CreateVersion7(), "USD", new UsageFigure(0.02m, 1_000, 200), null);
        var line = new ReplyUsageViewModel(detail.RunId, _ => Task.FromResult(detail), NullLogger.Instance, "vendor/sample-stronger");

        line.ShortModel.Should().Be("sample-stronger");
        line.ModelName.Should().Be("vendor/sample-stronger", "the whole name is in the tooltip");
        line.Figures.Should().BeNull("nothing is read until a person asks");
        line.HintName.Should().Be("Written by vendor/sample-stronger. Show what this reply used.");
        line.WrittenBy.Should().Be("Written by vendor/sample-stronger", "the popover and the tooltip name the whole model");

        await line.Toggle();

        line.Figures.Should().Be("1.2k tokens, USD 0.02");
        line.HintName.Should().Be("Written by vendor/sample-stronger, 1,200 tokens, USD 0.02. Show what this reply used.");

        line.Open = false;

        line.Figures.Should().Be("1.2k tokens, USD 0.02", "what was read once stays on the hint");
    }

    [Fact]
    public async Task A_price_the_server_does_not_know_is_left_out_and_the_bots_asked_are_counted()
    {
        var detail = new RunUsageDetail(Guid.CreateVersion7(), "USD", new UsageFigure(null, 10, 5), new UsageFigure(null, 400, 100));
        var line = new ReplyUsageViewModel(detail.RunId, _ => Task.FromResult(detail), NullLogger.Instance);

        await line.Toggle();

        line.Figures.Should().Be("500 tokens");
        line.ShortModel.Should().BeNull("a reply written before the model was kept names none");
        line.HintName.Should().Be("500 tokens. Show what this reply used.");
    }

    [Fact]
    public void A_reply_without_a_kept_model_or_figures_is_named_by_what_pressing_does()
    {
        var line = new ReplyUsageViewModel(Guid.CreateVersion7(), _ => Task.FromException<RunUsageDetail>(new InvalidOperationException()), NullLogger.Instance);

        line.ShortModel.Should().BeNull();
        line.Figures.Should().BeNull();
        line.HintName.Should().Be("Show what this reply used.");
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
    public void The_footer_stands_copy_then_another_reply_then_the_versions_then_the_hint()
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
            .Should().Equal(FooterPart.Copy, FooterPart.Regenerate, FooterPart.Versions, FooterPart.Usage);
        Enum.GetValues<FooterPart>().Should().Equal([FooterPart.Copy, FooterPart.Regenerate, FooterPart.Versions, FooterPart.Usage], "the line places its parts in this order");
    }

    [Fact]
    public void The_footer_holds_only_what_the_reply_and_the_screen_offer()
    {
        var reply = new ChatMessage("a", false, "Here is the plan.") { RunId = Guid.CreateVersion7(), FinishesReply = true };

        ChatRows.FooterParts(reply, canRegenerate: true, canSwitch: true, canReadUsage: true).Should().Equal(FooterPart.Copy);
        ChatRows.FooterParts(reply with { Streaming = true }, canRegenerate: true, canSwitch: true, canReadUsage: true).Should().BeEmpty();
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

        var shown = new[] { asked, answered }.ToChatMessages(reader: new UsageReader(mine, IsSpaceAdmin: false));

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

        var reply = new[] { answered }.ToChatMessages().Single();

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
