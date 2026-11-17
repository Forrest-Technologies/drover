namespace Drover.Ui.Pages.Extensions;

/// <summary>The sentences the extensions screen says about an extension.</summary>
public static class ExtensionWords
{
    #region Public Methods

    public static string Adds(ExtensionSummary extension) =>
        extension.BotName is { } bot ? $"Added the bot {bot}." : "The bot it added has been removed.";

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
