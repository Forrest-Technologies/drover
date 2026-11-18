namespace Drover.Presentation.ViewModels;

/// <summary>One bot a share can go to: its name, its look, and what choosing it does.</summary>
public sealed partial class ShareBotChoice(BotSummary bot, Action<BotSummary> choose)
{
    #region Properties

    public Guid Id => bot.Id;

    public string Name => bot.Name;

    public int Hue => BotIdentity.Hue(bot.Id);

    #endregion

    #region Public Methods

    [RelayCommand]
    public void Choose() => choose(bot);

    #endregion
}
