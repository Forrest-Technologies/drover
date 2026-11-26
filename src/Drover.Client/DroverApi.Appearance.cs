namespace Drover.Client;

public sealed partial class DroverApi
{
    #region Appearance

    public async Task<AppearanceSetting?> GetAppearance(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/account/appearance", cancellationToken);
        return await response.ReadValueUnless<AppearanceSetting>(HttpStatusCode.NotFound, cancellationToken);
    }

    public Task SetAppearance(AppearanceSetting setting, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Put, "api/account/appearance", setting, cancellationToken);

    #endregion
}
