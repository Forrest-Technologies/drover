namespace Drover.Ui.Bots;

/// <summary>The sentences the bots screen says about a bot file.</summary>
public static class BotFileWords
{
    #region Public Methods

    public static string Hidden(int count) =>
        count == 1
            ? "These instructions hold 1 character that does not show on a screen. It is written out where it stands, like [U+200B]."
            : $"These instructions hold {count} characters that do not show on a screen. Each is written out where it stands, like [U+200B].";

    public static string Imported(BotSummary bot) => $"{bot.Name} is imported and paused. Set it up, then turn it on.";

    #endregion
}
