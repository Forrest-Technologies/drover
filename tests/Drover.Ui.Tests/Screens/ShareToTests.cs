namespace Drover.Ui.Tests.Screens;

public sealed class ShareToTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Properties

    private static BotSummary Marshal { get; } =
        new(botId, "Marshal", "marshal", "Sample instructions", Guid.Parse("0196f1a0-0000-7000-8000-000000000003"), "sample-default", "active");

    private ShareInbox Inbox => Services.GetRequiredService<ShareInbox>();

    #endregion

    #region Public Methods

    [Fact]
    public void With_nothing_shared_the_screen_says_so_and_points_back_to_the_crew()
    {
        SignedIn();

        var page = Render<ShareTo>();

        page.Find("h1").TextContent.Should().Be("Share to Drover");
        page.Find(".dv-empty-cause").TextContent.Should().Be("Nothing is waiting to be shared. Share text or a file to Drover from another app.");
        page.Find("a.dv-empty-action").GetAttribute("href").Should().Be("/");
        page.FindAll(".dv-share-bot").Should().BeEmpty();
    }

    [Fact]
    public void What_was_shared_is_shown_and_choosing_a_bot_opens_its_conversation_with_the_share_for_it_alone()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var shared = new SharedDraft("Sample words from another app", [new SampleSharedFile("notes.txt", "one")]);
        Inbox.Offer(shared);
        var page = Render<ShareTo>();
        page.WaitForElement(".dv-share-bot");

        page.Find("section[aria-label='What you shared'] .dv-share-text").TextContent.Should().Be("Sample words from another app");
        page.Find(".dv-share-files li").TextContent.Should().Be("notes.txt 3 bytes");
        page.Find("ul[aria-labelledby=share-ask] button").TextContent.Trim().Should().Be("Marshal");
        page.Find(".dv-share-note").TextContent.Should().Contain("Nothing is sent until you send it.");
        page.Find(".dv-share-bot").Click();

        Address.Should().Be($"talk/{botId}");
        Inbox.TakeFor(Guid.NewGuid()).Should().BeNull("no other conversation gets it");
        Inbox.TakeFor(botId).Should().Be(shared);
        Inbox.TakeFor(botId).Should().BeNull("it is handed over once");
    }

    [Fact]
    public void Discarding_forgets_what_was_shared()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var page = Render<ShareTo>();
        page.WaitForElement(".dv-share-bot");

        page.Find(".dv-share-actions button").Click();

        Inbox.Waiting.Should().BeNull();
        Address.Should().BeEmpty();
    }

    [Fact]
    public void A_crew_that_cannot_be_loaded_says_so_and_keeps_what_was_shared()
    {
        SignedIn();
        Server.Refuse("GET", $"{space}/bots", HttpStatusCode.ServiceUnavailable, "Sample outage.");
        Inbox.Offer(new SharedDraft("Sample words from another app", []));

        var page = Render<ShareTo>();

        page.WaitForAssertion(() => page.Find("h2").TextContent.Should().Be("Drover could not load your crew"));
        page.Find(".dv-empty button").TextContent.Trim().Should().Be("Try again");
        Inbox.Waiting.Should().NotBeNull();
    }

    #endregion
}
