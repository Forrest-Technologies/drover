namespace Drover.Web;

/// <summary>
/// Notifications in a browser or an installed web app, through the service worker and the Push API. The script
/// that speaks to the browser is <c>push.js</c>; this class only carries its answers. A browser that fails a call
/// throws <see cref="JSException"/>, which the control turns into a sentence.
/// </summary>
public sealed class BrowserPushRegistration(IJSRuntime js, ILogger<BrowserPushRegistration> logger) : IPushRegistration, IAsyncDisposable
{
    #region Private Fields

    private const string ModulePath = "./push.js";

    private IJSObjectReference? module;

    #endregion

    #region Interface Implementations

    public async Task<DevicePushCheck> Check(CancellationToken cancellationToken = default)
    {
        var answer = await Call<CheckAnswer>("check", cancellationToken);
        return new DevicePushCheck(
            answer.Support switch
            {
                "available" => DevicePushSupport.Available,
                "blocked" => DevicePushSupport.Blocked,
                "needs-home-screen" => DevicePushSupport.NeedsHomeScreen,
                _ => DevicePushSupport.NotSupported,
            },
            answer.Endpoint);
    }

    public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default) =>
        Call<PushDeviceRequest?>("subscribe", cancellationToken, publicKey);

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default) =>
        Call<string?>("unsubscribe", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    #endregion

    #region Private Methods

    private async Task<T> Call<T>(string function, CancellationToken cancellationToken, params object?[] arguments)
    {
        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
            return await module.InvokeAsync<T>(function, cancellationToken, arguments);
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The browser failed the notification call {Function}", function);
            throw;
        }
    }

    #endregion

    #region Answers

    private sealed record CheckAnswer(string Support, string? Endpoint);

    #endregion
}
