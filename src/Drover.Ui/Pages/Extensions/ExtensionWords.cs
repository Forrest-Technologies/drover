namespace Drover.Ui.Pages.Extensions;

/// <summary>The sentences the extensions screen says about an extension.</summary>
public static class ExtensionWords
{
    #region Public Methods

    public static string Adds(ExtensionSummary extension) =>
        extension.BotName is { } bot ? $"Added the bot {bot}."
        : !extension.AddsBot && extension.Connection is { } connection ? $"Names the tool server {connection.Name}."
        : "The bot it added has been removed.";

    /// <summary>How Drover proves who is calling a tool server, in words a person reads before connecting.</summary>
    public static string Auth(string auth, string? headerName) => auth switch
    {
        "header" => $"A header named {headerName}. You type its value when you connect, and it is kept secret.",
        "oauth" => "A sign-in at the server. Each person signs in as themselves on the Tools screen.",
        _ => "Nothing. The server asks for no credential.",
    };

    public static string ConnectionStatus(ExtensionConnectionSummary connection) => connection.Status switch
    {
        "ready" => "Connected",
        "failed" => "Not answering",
        _ => "Connected, not listed yet",
    };

    public static string ToolState(SuggestedToolState tool) =>
        tool.ToolId is null ? "The server does not list this tool."
        : tool.Grant is "ask" ? "The bot has it, and asks first."
        : tool.Grant is "allow" ? "The bot has it."
        : string.Empty;

    public static string Status(ExtensionSummary extension) =>
        extension.BotStatus is "active" ? "On" : "Paused";

    public static string Imported(ExtensionSummary extension) =>
        extension.BotName is { } bot
            ? $"{extension.Name} is imported. {bot} is paused: set it up, then turn it on."
            : $"{extension.Name} is imported.";

    public static string Hidden(int count) =>
        count == 1
            ? "These instructions hold 1 character that does not show on a screen. It is written out where it stands, like [U+200B]."
            : $"These instructions hold {count} characters that do not show on a screen. Each is written out where it stands, like [U+200B].";

    #endregion
}
