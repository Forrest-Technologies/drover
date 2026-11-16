namespace Drover.Ui.Tests.Chat;

/// <summary>
/// The conversation's menu: its one item says what choosing it will do, and the choice reaches the server as the
/// signed-in person's own. The bot is a sample.
/// </summary>
public sealed class BotNotifyMenuTests : ScreenTest
{
    #region Private Fields

    private static readonly BotSummary bot = new(
        Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1"), "Sample bot", "sample-bot", string.Empty, Guid.Empty, "sample-model", "active");

    private static readonly string path = $"/api/spaces/{SpaceId}/bots/{bot.Id}/notify";

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_item_says_what_it_will_do_and_choosing_it_tells_the_server()
    {
        SignedIn();
        await Services.GetRequiredService<SessionState>().Load(Xunit.TestContext.Current.CancellationToken);
        Server.Answer("GET", path, new BotNotifySetting(true));
        Server.Answer("PUT", path, new BotNotifySetting(false));

        var menu = Render<BotNotifyMenu>(set => set.Add(view => view.Bot, bot));
        menu.WaitForAssertion(() => menu.Find("[role=menuitem]").TextContent.Trim().Should().Be("Stop notifying me about Sample bot"));
        menu.Find("[aria-haspopup=menu]").TextContent.Should().Be("More");
        menu.Find("[aria-haspopup=menu]").GetAttribute("aria-label").Should().Be("More about Sample bot");

        await menu.Find("[role=menuitem]").ClickAsync();

        menu.WaitForAssertion(() => menu.Find("[role=menuitem]").TextContent.Trim().Should().Be("Notify me about Sample bot"));
        Server.Requests.Should().Contain(request => request.Method == "PUT" && request.Path == path && request.Body.Contains("false"));
    }

    [Fact]
    public async Task Nothing_shows_until_the_server_has_said_which_way_it_is_set()
    {
        SignedIn();
        await Services.GetRequiredService<SessionState>().Load(Xunit.TestContext.Current.CancellationToken);
        Server.Answer("GET", path, HttpStatusCode.NotFound);

        var menu = Render<BotNotifyMenu>(set => set.Add(view => view.Bot, bot));

        menu.WaitForAssertion(() => Server.Requests.Should().Contain(request => request.Path == path));
        menu.FindAll("[role=menu]").Should().BeEmpty();
    }

    #endregion
}
