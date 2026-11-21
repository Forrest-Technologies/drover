namespace Drover.Presentation.Tests.Routing;

/// <summary>
/// The native client's table of screens, read from its source. Every address the retired web client had a page at
/// still opens a screen, written the same way, and so does every address the product writes. A new screen may add
/// an address; none of these may go.
/// </summary>
public sealed partial class NativeScreensTests
{
    #region Private Fields

    // The page addresses of the web client when it was retired on 2026-11-20, but its gallery of starter pieces.
    // People kept links to these; each must keep opening a screen.
    private static readonly string[] retiredWebClientPages =
    [
        "/", "/bots", "/bots/{BotId:guid}", "/bots/{BotId:guid}/sandbox", "/dev/gallery", "/extensions",
        "/extensions/edit", "/models", "/setup", "/setup/bot", "/setup/model", "/share", "/sign-in", "/status",
        "/talk/{BotId:guid}", "/talk/{BotId:guid}/activity/{ActivityRunId:guid}", "/talk/{BotId:guid}/side/{SideChat}",
        "/talk/{BotId:guid}/work/{WorkBotId:guid}/{WorkThreadId:guid}", "/talk/{BotId:guid}/{Rail}",
        "/threads/{FormerThreadId:guid}", "/tools", "/usage",
    ];

    // The web client still makes a bot when New bot is pressed. The native client opens the editor on a bot that is
    // not made yet, at /bots/new, and nothing is stored until it is saved. Project screens are native too. The Razor
    // client does not grow in this change.
    private static readonly string[] onlyOnNative = ["/bots/new", "/projects", "/projects/{ProjectId:guid}"];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_address_the_retired_web_client_had_a_page_at_opens_a_native_screen()
    {
        NativePatterns().Should().Contain(retiredWebClientPages, "an address is a promise: a link someone kept must open");
    }

    [Fact]
    public void Every_address_the_product_writes_opens_a_native_screen()
    {
        var table = new RouteTable<int>();
        foreach (var (pattern, index) in NativePatterns().Select((pattern, index) => (pattern, index)))
        {
            table.Map(index, pattern);
        }

        var bot = Guid.NewGuid();
        var other = Guid.NewGuid();
        string[] written =
        [
            Addresses.Home, Addresses.SignIn, Addresses.Setup, Addresses.Share, Addresses.SetupModel, Addresses.SetupBot,
            Addresses.Bots, Addresses.Projects, Addresses.Project(bot), Addresses.Models, Addresses.Tools, Addresses.Extensions, Addresses.ExtensionEditor,
            Addresses.ExtensionFromBot(bot), Addresses.Status, Addresses.Usage, Addresses.Conversation(other),
            Addresses.Talk(bot), Addresses.Summary(bot), Addresses.Memory(bot), Addresses.Screen(bot), Addresses.Activity(bot),
            Addresses.Activity(bot, other), Addresses.Files(bot), Addresses.Moment(bot, other, Guid.NewGuid(), false),
            Addresses.SideChats(bot), Addresses.NewSideChat(bot), Addresses.SideChat(bot, other), Addresses.Work(bot, other, other),
            Addresses.Bot(bot), Addresses.NewBot(), Addresses.NewBot(bot), Addresses.BotMemory(bot), Addresses.BotSandbox(bot), Addresses.BotTerminal(bot),
            Addresses.UsageFor("7d"), Addresses.UsageFor("7d", bot), Addresses.ToolSignIn("done", "sample"),
            Addresses.ToolSignIn("failed", null), Addresses.ToolSignInReturn("sample ticket"),
        ];

        written.Where(address => table.Match(address) is null).Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static List<string> NativePatterns() =>
        [.. Pattern().Matches(File.ReadAllText(Path.Combine(Repository.NativeFolder, "Shell", "Screens.cs")))
            .Select(match => match.Groups[1].Value)];

    // A pattern is a quoted string that starts with a slash, inside the table.
    [GeneratedRegex("""
        "(/[A-Za-z0-9{}:/-]*)"
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Pattern();

    #endregion
}
