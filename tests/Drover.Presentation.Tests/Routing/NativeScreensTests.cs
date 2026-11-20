namespace Drover.Presentation.Tests.Routing;

/// <summary>
/// The native client's table of screens, read from its source, held against the web client's pages: every address
/// the web client has a page at opens a screen in the native client, written the same way.
/// </summary>
public sealed partial class NativeScreensTests
{
    #region Private Fields

    private static readonly string[] onlyOnTheWeb = ["/dev/starters"];

    // The web client still makes a bot when New bot is pressed. The native client opens the editor on a bot that is
    // not made yet, at this address, and nothing is stored until it is saved.
    private static readonly string[] onlyOnNative = ["/bots/new"];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_address_the_web_client_has_a_page_at_opens_a_native_screen()
    {
        var web = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "Drover.Ui"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => PageDirective().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Except(onlyOnTheWeb)
            .ToList();

        web.Should().NotBeEmpty();
        NativePatterns().Except(onlyOnNative).Should().BeEquivalentTo(web, "an address is a promise: a link someone kept must open in either client");
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
            Addresses.Bots, Addresses.Models, Addresses.Tools, Addresses.Extensions, Addresses.ExtensionEditor,
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

    [GeneratedRegex("""
        @page\s+"([^"]+)"
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex PageDirective();

    // A pattern is a quoted string that starts with a slash, inside the table.
    [GeneratedRegex("""
        "(/[A-Za-z0-9{}:/-]*)"
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Pattern();

    #endregion
}
