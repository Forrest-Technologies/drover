namespace Drover.Presentation.ViewModels;

/// <summary>
/// The Sandbox section of the bot editor: whether the bot has a place to run code and keep files, and who shares it.
/// </summary>
public sealed class SandboxSectionViewModel(BotEditorViewModel editor)
{
    #region Private Fields

    private const string NoProvider =
        "No sandbox provider is set up on this server, so no bot has a sandbox to use yet. This choice is saved for when one is added.";

    private const string DesktopWords =
        "Gives the sandbox a screen the bot sees in screenshots and uses with the mouse and keyboard. It costs more to run. Screenshots go to a model as they are, so they can show anything on that screen, passwords included. Changing this replaces the sandbox, and its files, on its next use.";

    private static readonly string[] policies = ["none", "private", "subtree", "named"];

    private static readonly SegmentOption<string>[] allNetworks = [new("off", "Off"), new("allowlist", "Named hosts"), new("open", "Open")];

    private static readonly SegmentOption<string>[] closedNetworks = [.. allNetworks.Where(network => network.Value is not "open")];

    private static readonly ToolWords sandboxWords = new(
        "This bot may not use its sandbox yet. Allow its sandbox tools to let it run commands and use files.",
        "The sandbox tools will be allowed when you save.",
        "May run commands and use files.",
        "use its sandbox",
        "sandbox tools");

    private static readonly ToolWords screenWords = new(
        "This bot may not use the screen yet. Allow its screen tools to let it see and drive the screen.",
        "The screen tools will be allowed when you save.",
        "May use the screen.",
        "use the screen",
        "screen tools");

    #endregion

    #region Properties

    public static string OpenSandbox => "Open the sandbox";

    public static string PolicyLabel => "Sandbox";

    public static IReadOnlyList<ChoiceOption> Policies => [.. policies.Select(policy => new ChoiceOption(policy, BotSummaries.SandboxPolicy(policy)))];

    public static string NoOtherBots => "There is no other bot in this space to share with yet.";

    public static string ShareLegend => "Share with";

    public static string NetworkWord => "Network";

    public static string NetworkLabel => "Sandbox network";

    public static string NoOpenNetwork => "Open internet access is not available on this server.";

    public static string HostsLabel => "Hosts it may reach";

    public static string HostsHint => "One host name to a line, such as example.com.";

    public static string IdleLabel => "Stop after idle minutes";

    public static string DesktopLabel => "Desktop";

    public static string DesktopHint => DesktopWords;

    public static string OpenModel => "Open the Model section";

    public static string OpenTools => "Open the Tools section";

    public static string NoDesktop => "A sandbox with a screen is not available on this server.";

    public static string SandboxGroupName => "What this bot may do in its sandbox";

    public static string ScreenGroupName => "What this bot may do on its screen";

    public static string TakeOver => "Asking a person to take over the screen is a tool of its own, allowed in the Tools section.";

    public static string CannotSee =>
        "The model this bot uses cannot see images, so the bot cannot use the screen. In the Model section, choose a model to read the screen for it, or a model that can see.";

    /// <summary>The note's live region stays in place; only its text comes and goes.</summary>
    public string? ProviderNote => editor.SandboxOptions.Available ? null : NoProvider;

    /// <summary>How the sandbox is now, with a way to its screen, when the bot has one.</summary>
    public string? Live => editor.SandboxStatus is { Available: true, HasSandbox: true } live ? SandboxWords.Summary(live, DateTimeOffset.UtcNow) : null;

    public string SandboxAddress => Addresses.BotSandbox(editor.BotId);

    public string Meaning => Draft.SandboxPolicy switch
    {
        "private" => "Only this bot uses its sandbox. Nothing another bot does can touch its files.",
        "subtree" => "This bot and every bot beneath it work in the same files and can change each other's work.",
        "named" => "The bots you choose work in the same files as this one and can change its work.",
        _ => "This bot runs no code and keeps no files of its own.",
    };

    public bool SharingShown => Draft.SandboxPolicy is "named";

    public IReadOnlyList<BotSummary> OtherBots => editor.OtherBots;

    /// <summary>The rest of the settings mean something only for a bot that has a sandbox.</summary>
    public bool SettingsShown => Draft.SandboxPolicy is not "none";

    // A bot already set to open keeps the choice visible, so the editor shows what is stored; the server refuses to save it.
    public IReadOnlyList<SegmentOption<string>> Networks => editor.SandboxOptions.OpenNetwork || Draft.Network is "open" ? allNetworks : closedNetworks;

    public bool NoOpenNetworkShown => !editor.SandboxOptions.OpenNetwork;

    public bool HostsShown => Draft.Network is "allowlist";

    public string IdlePlaceholder => editor.SandboxOptions.MaxIdleMinutes > 0 ? $"Up to {editor.SandboxOptions.MaxIdleMinutes}" : "The server's own limit";

    // A bot already set to have a screen keeps the switch, so it can be turned off; the server refuses to save it on.
    public bool DesktopShown => editor.SandboxOptions.Desktop || Draft.Desktop;

    public bool ReadByShown => Draft.Desktop && Draft.ScreenReader is not null;

    public string ReadBy => $"{editor.ScreenReaderName ?? Draft.ScreenReader!.Model} reads the screen for this bot. Change it in the Model section.";

    public bool NoDesktopShown => !editor.SandboxOptions.Desktop;

    public ToolStanding SandboxStanding => SandboxGrants.Standing(editor.Tools, SandboxGrants.SandboxTools, Draft.StagedGrants);

    public ToolStanding ScreenStanding => SandboxGrants.Standing(editor.Tools, SandboxGrants.ScreenTools, Draft.StagedGrants);

    public bool SandboxGrantShown => editor.SandboxOptions.Available && SandboxStanding is not ToolStanding.Unknown;

    public bool ScreenGrantShown => editor.SandboxOptions.Available && Draft.Desktop && ScreenStanding is not ToolStanding.Unknown;

    public string SandboxGrantWords => Words(SandboxStanding, SandboxGrants.SandboxTools, sandboxWords);

    public string ScreenGrantWords => Words(ScreenStanding, SandboxGrants.ScreenTools, screenWords);

    /// <summary>The button beside the sandbox grant: "Allow sandbox tools", or the way to the Tools section, or none.</summary>
    public string? SandboxGrantAction => Action(SandboxStanding, "Allow remaining sandbox tools", "Allow sandbox tools");

    public string? ScreenGrantAction => Action(ScreenStanding, "Allow remaining screen tools", "Allow screen tools");

    public bool CannotSeeShown => Draft.Desktop && editor.ModelSeesImages is false && Draft.ScreenReader is null;

    private BotDraft Draft => editor.Draft!;

    #endregion

    #region Public Methods

    public static bool Allows(ToolStanding standing) => standing is ToolStanding.NotAllowed or ToolStanding.Partial;

    public bool SharesWith(Guid botId) => Draft.SandboxSharedWith.Contains(botId);

    public void Share(Guid botId, bool shared)
    {
        Draft.SandboxSharedWith = shared
            ? [.. Draft.SandboxSharedWith.Append(botId).Distinct()]
            : [.. Draft.SandboxSharedWith.Where(id => id != botId)];
        editor.Changed();
    }

    public void AllowSandboxTools() => Allow(SandboxGrants.SandboxTools);

    public void AllowScreenTools() => Allow(SandboxGrants.ScreenTools);

    #endregion

    #region Private Methods

    private string Words(ToolStanding standing, IReadOnlyList<string> names, ToolWords words) => standing switch
    {
        ToolStanding.NotAllowed => words.NotAllowed,
        ToolStanding.Partial => $"Some {words.Tools} are allowed. See each grant under Tools.",
        ToolStanding.AllowedOnSave => words.OnSave,
        ToolStanding.ParentForbids =>
            $"{SandboxGrants.Forbidder(editor.Tools, names) ?? "The bot above this one"} does not allow the {words.Tools}, so this bot cannot {words.Use}.",
        _ => SandboxGrants.Asks(editor.Tools, names, Draft.StagedGrants) ? $"{words.Allowed} It asks a person before each use." : words.Allowed,
    };

    private static string? Action(ToolStanding standing, string remaining, string all) => standing switch
    {
        ToolStanding.Partial => remaining,
        ToolStanding.NotAllowed => all,
        ToolStanding.Allowed => OpenTools,
        _ => null,
    };

    private void Allow(IReadOnlyList<string> names)
    {
        Draft.StagedGrants = SandboxGrants.Stage(editor.Tools, names, Draft.StagedGrants);
        editor.Changed();
    }

    #endregion

    #region Nested Types

    private sealed record ToolWords(string NotAllowed, string OnSave, string Allowed, string Use, string Tools);

    #endregion
}
