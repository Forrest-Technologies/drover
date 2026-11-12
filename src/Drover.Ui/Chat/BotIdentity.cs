namespace Drover.Ui.Chat;

/// <summary>
/// The parts of a bot's look that follow from its id, so the same bot looks the same on every device.
/// </summary>
public static class BotIdentity
{
    #region Public Methods

    /// <summary>The bot's identity hue. <see cref="Avatar"/> folds any number into its twelve steps.</summary>
    public static int Hue(Guid botId) => botId.ToByteArray().Aggregate(0, (sum, part) => sum + part);

    #endregion
}
