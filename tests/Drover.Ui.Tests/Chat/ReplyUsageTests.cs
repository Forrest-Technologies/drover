namespace Drover.Ui.Tests.Chat;

/// <summary>
/// What a finished reply used, under its words: closed until asked for, read once when opened, and said in the same
/// words as the usage page. Every figure is a sample.
/// </summary>
public sealed class ReplyUsageTests : BunitContext
{
    #region Constructors

    public ReplyUsageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Public Methods

    [Fact]
    public void The_line_is_read_only_when_a_person_opens_it_and_says_the_runs_stored_figures()
    {
        var runId = Guid.CreateVersion7();
        List<Guid> asked = [];
        var line = Render<ReplyUsage>(set => set
            .Add(view => view.RunId, runId)
            .Add(view => view.Load, id =>
            {
                asked.Add(id);
                return Task.FromResult(new RunUsageDetail(id, "USD", new(0.02m, 1_200, 340), new(0.10m, 4_000, 900)));
            }));
        var toggle = line.Find("button.dv-reply-usage-toggle");

        toggle.TextContent.Trim().Should().Be("What this reply used");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        asked.Should().BeEmpty("a closed line asks the server for nothing");

        toggle.Click();
        line.WaitForAssertion(() => line.FindAll(".dv-reply-usage-line").Select(text => text.TextContent).Should().Equal(
            "This reply: USD 0.02, 1,200 tokens in, 340 out.",
            "With the bots it asked: USD 0.10, 4,000 tokens in, 900 out."));
        line.Find("button.dv-reply-usage-toggle").GetAttribute("aria-expanded").Should().Be("true");
        line.Find("button.dv-reply-usage-toggle").GetAttribute("aria-controls").Should().Be(line.Find(".dv-reply-usage-body").Id);

        line.Find("button.dv-reply-usage-toggle").Click();
        line.Find("button.dv-reply-usage-toggle").Click();
        asked.Should().Equal([runId], "what a finished run used does not change, so it is read once");
    }

    [Fact]
    public void A_failed_read_says_what_failed_and_can_be_tried_again()
    {
        var reachable = false;
        var line = Render<ReplyUsage>(set => set
            .Add(view => view.RunId, Guid.CreateVersion7())
            .Add(view => view.Load, id => reachable
                ? Task.FromResult(new RunUsageDetail(id, "USD", new(0.01m, 10, 1), null))
                : Task.FromException<RunUsageDetail>(new HttpRequestException("The server cannot be reached."))));

        line.Find("button.dv-reply-usage-toggle").Click();

        line.WaitForAssertion(() => line.Find(".dv-reply-usage-line").GetAttribute("role").Should().Be("alert"));
        line.Find(".dv-reply-usage-line").TextContent.Should().NotBeEmpty();
        var retry = line.FindAll(".dv-reply-usage-body button").Should().ContainSingle().Subject;
        retry.TextContent.Trim().Should().Be("Try again");

        reachable = true;
        retry.Click();

        line.WaitForAssertion(() => line.Find(".dv-reply-usage-line").TextContent.Should().Be("This reply: USD 0.01, 10 tokens in, 1 out."));
    }

    [Fact]
    public void Only_a_reply_that_ended_shows_the_control()
    {
        ChatMessage[] messages =
        [
            new("a", false, "Thinking aloud.") { RunId = Guid.CreateVersion7() },
            new("b", false, "Here is the plan.") { RunId = Guid.CreateVersion7(), EndsReply = true },
        ];

        var list = Render<MessageList>(set => set
            .Add(view => view.Messages, messages)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.LoadUsage, id => Task.FromResult(new RunUsageDetail(id, "USD", new(0.01m, 1, 1), null))));

        list.FindAll("button.dv-reply-usage-toggle").Should().ContainSingle();
        list.FindAll("article")[1].QuerySelector("button.dv-reply-usage-toggle").Should().NotBeNull();
    }

    #endregion
}
