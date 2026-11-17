namespace Drover.Engine.Extensions;

public static class ExtensionViews
{
    #region Public Methods

    public static ExtensionBotPreview ToPreview(this BotTemplate bot, ModelChoice? model)
    {
        var (instructions, hidden) = ExtensionPackages.ShowHidden(bot.Instructions);
        return new(bot.Name.Trim(), bot.Description, instructions, hidden) { Model = model?.Model, ModelConnection = model?.Connection.Name };
    }

    public static ExtensionSummary ToSummary(this InstalledExtension installed) =>
        new(installed.Install.Id, installed.Install.PackageId, installed.Install.Name, installed.Install.Description, installed.Install.InstalledAt)
        {
            BotId = installed.BotName is null ? null : installed.Install.BotId,
            BotName = installed.BotName,
            BotStatus = installed.BotStatus is { } status ? Words.Of(status) : null,
        };

    #endregion
}
