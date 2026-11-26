namespace Drover.Ui.Pages.Extensions;

/// <summary>The sentences the extensions screen says about an extension.</summary>
public static class ExtensionWords
{
    #region Public Methods

    /// <summary>What the extension holds, in one line: its tool server, its themes, its rules.</summary>
    public static string Holds(ExtensionConnectionSummary? connection, ExtensionParts parts)
    {
        List<string> held = [];

        if (connection is not null)
        {
            held.Add($"Tool server {connection.Name}");
        }

        if (parts.Themes.Count > 0)
        {
            held.Add(Count(parts.Themes.Count, "theme"));
        }

        if (parts.Rules.Count > 0)
        {
            held.Add(Count(parts.Rules.Count, "rule"));
        }

        return held.Count > 0 ? string.Join(" · ", held) : "Holds nothing now. You can remove it.";
    }

    public static string Holds(ExtensionSummary extension) => Holds(extension.Connection, extension.Parts);

    /// <summary>How Drover signs in to a tool server, in words a person reads before connecting.</summary>
    public static string Auth(string auth, string? headerName, bool asksForValue) => auth switch
    {
        "header" when asksForValue => $"A header named {headerName}. You type its value when you connect, and it is kept secret.",
        "header" => $"A header named {headerName}, made from a secret setting you fill in when you connect. It is kept secret.",
        "oauth" => "A sign-in at the server. Each person signs in as themselves on the Tools screen.",
        _ => "Nothing. The server asks for no credential.",
    };

    public static string ConnectionStatus(ExtensionConnectionSummary connection) => connection.Status switch
    {
        "ready" => "Connected",
        "failed" => "Not answering",
        _ => "Connected, not listed yet",
    };

    public static string ToolState(SuggestedToolState tool) => tool.ToolId is null ? "The server does not list this tool." : string.Empty;

    /// <summary>What a setting's field says under it: the file's own help, and for a secret that it is not kept in sight.</summary>
    public static string SettingHint(ExtensionSetting setting) =>
        setting.Type is ExtensionSetting.Secret
            ? $"{setting.Help} Stored encrypted. It is not shown again.".TrimStart()
            : setting.Help;

    public static string Imported(ExtensionSummary extension) =>
        extension.Connection is { Available: true } ? $"{extension.Name} is imported. Connect its tool server below."
        : $"{extension.Name} is imported.";

    public static string Removed(ExtensionSummary extension) => $"{extension.Name} is removed.";

    public static string RemoveQuestion(ExtensionSummary extension) => $"Remove {extension.Name}?";

    /// <summary>What goes with the extension, said before a person removes it.</summary>
    public static string RemoveConsequence(ExtensionSummary extension)
    {
        List<string> gone = [];

        if (extension.Connection?.ConnectionId is not null)
        {
            gone.Add($"The connection to {extension.Connection.Name} is removed, with its credentials and every bot's grants on its tools.");
        }

        if (extension.Parts.Themes.Count > 0)
        {
            gone.Add("Its themes are no longer offered. A person using one goes back to the default look.");
        }

        if (extension.Parts.Rules.Count > 0)
        {
            gone.Add("Its rules are no longer suggested. A rule already added to a bot stays.");
        }

        return gone.Count > 0 ? string.Join(' ', gone) : "It holds nothing, so nothing else changes.";
    }

    public static string Count(int count, string thing) => count == 1 ? $"1 {thing}" : $"{count} {thing}s";

    #endregion
}
