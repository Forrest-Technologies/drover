namespace Drover.Ui.Tests.Chat;

/// <summary>The word beside a bot and the line a failed run ends with: moved here with the starter for #148.</summary>
public sealed class AttentionComponentTests : BunitContext
{
    #region Constructors

    public AttentionComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Public Methods

    [Fact]
    public void A_quiet_bot_shows_no_attention_and_a_waiting_one_says_so_in_words()
    {
        Render<AttentionNote>(set => set.Add(note => note.Attention, Attention.None)).Markup.Trim().Should().BeEmpty();
        Render<AttentionNote>(set => set.Add(note => note.Attention, Attention.WaitingForYou)).Find(".dv-badge").TextContent.Should().Be("Waiting for you");
    }

    [Fact]
    public void A_failed_run_says_what_failed_and_offers_one_way_forward()
    {
        var retried = false;
        var line = Render<FailureLine>(set => set
            .Add(failure => failure.Cause, FailureCause.ModelUnreachable)
            .Add(failure => failure.BotName, "Sample bot")
            .Add(failure => failure.Reported, "connection refused")
            .Add(failure => failure.OnRetry, () => retried = true));

        line.Find("h3").TextContent.Should().StartWith("Sample bot could not reach its model.");
        line.FindAll("button").Select(button => button.TextContent.Trim()).Should().Contain("Try again");
        line.Markup.Should().NotContain("connection refused", "the server's report is shown only when asked for");

        line.FindAll("button").First(button => button.TextContent.Trim() == "Try again").Click();

        retried.Should().BeTrue();
    }

    [Fact]
    public void While_a_retry_is_asked_for_the_button_keeps_focus_says_it_is_busy_and_a_second_press_does_nothing()
    {
        var presses = 0;
        var line = Render<FailureLine>(set => set
            .Add(failure => failure.Cause, FailureCause.Unknown)
            .Add(failure => failure.BotName, "Sample bot")
            .Add(failure => failure.Retrying, true)
            .Add(failure => failure.OnRetry, () => presses++));

        var button = line.Find("button");
        button.Click();

        button.HasAttribute("disabled").Should().BeFalse();
        button.GetAttribute("aria-disabled").Should().Be("true");
        presses.Should().Be(0);
    }

    #endregion
}
