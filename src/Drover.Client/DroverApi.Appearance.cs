namespace Drover.Client;

/// <summary>The theme a person keeps for every device that follows their account.</summary>
public sealed partial class DroverApi
{
    #region Appearance

    /// <summary>The theme the person keeps for their devices, or null when they keep none.</summary>
    public async Task<AppearanceSetting?> GetAppearance(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/account/appearance", cancellationToken);
        return await response.ReadValueUnless<AppearanceSetting>(HttpStatusCode.NotFound, cancellationToken);
    }

    public Task SetAppearance(AppearanceSetting setting, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Put, "api/account/appearance", setting, cancellationToken);

    #endregion
}
