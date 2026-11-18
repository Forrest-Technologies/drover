namespace Drover.Engine.Extensions;

public static class ExtensionViews
{
    #region Public Methods

    public static ExtensionBotPreview ToPreview(this BotTemplate bot, ModelChoice? model)
    {
        var (instructions, hidden) = ExtensionPackages.ShowHidden(bot.Instructions);
        return new(bot.Name.Trim(), bot.Description, instructions, hidden) { Model = model?.Model, ModelConnection = model?.Connection.Name };
    }

    public static ExtensionConnectionPreview ToPreview(this ConnectionRecipe recipe, bool available)
    {
        var address = new Uri(recipe.Url.Trim());
        return new(recipe.Name.Trim(), recipe.Url.Trim(), address.Host, recipe.Auth, recipe.HeaderName, ExtensionPackages.IsLocal(address), available)
        {
            SuggestedTools = recipe.SuggestedTools,
        };
    }

    public static ExtensionConnectionSummary ToSummary(this ExtensionConnectionPreview preview) =>
        new(preview.Name, preview.Url, preview.Host, preview.Auth, preview.HeaderName, preview.IsLocal, preview.Available);

    public static ExtensionSummary ToSummary(this ExtensionState state)
    {
        var installed = state.Installed;
        return new(installed.Install.Id, installed.Install.PackageId, installed.Install.Name, installed.Install.Description, installed.Install.InstalledAt)
        {
            AddsBot = installed.Install.BotId is not null,
            BotId = installed.BotName is null ? null : installed.Install.BotId,
            BotName = installed.BotName,
            BotStatus = installed.BotStatus is { } status ? Words.Of(status) : null,
            Connection = state.Connection,
        };
    }

    #endregion
}
