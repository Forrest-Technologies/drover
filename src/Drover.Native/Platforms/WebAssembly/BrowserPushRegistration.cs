namespace Drover.Native.Platform;

/// <summary>
/// Notifications in a browser or an installed web app, through the service worker and the Push API. The script that
/// speaks to the browser is the web client's own <c>push.js</c>, shared; this class only carries its answers. A
/// browser that fails a call is reported as <see cref="DeviceCallFailedException"/>, which the notifications control
/// turns into a sentence.
/// </summary>
internal sealed partial class BrowserPushRegistration(ILogger<BrowserPushRegistration> logger) : IPushRegistration
{
    #region Private Fields

    private const string Module = "drover-push";

    // Relative to the runtime's own folder, _framework, beside which the page's files sit.
    private const string ModulePath = "../push.js";

    private Task<JSObject>? loaded;

    #endregion

    #region Interface Implementations

    public async Task<DevicePushCheck> Check(CancellationToken cancellationToken = default)
    {
        using var answer = await Call(CheckWith, "check");
        return new DevicePushCheck(
            answer.GetPropertyAsString("support") switch
            {
                "available" => DevicePushSupport.Available,
                "blocked" => DevicePushSupport.Blocked,
                "needs-home-screen" => DevicePushSupport.NeedsHomeScreen,
                _ => DevicePushSupport.NotSupported,
            },
            answer.GetPropertyAsString("endpoint"));
    }

    public async Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default)
    {
        using var answer = await Call(() => SubscribeWith(publicKey), "subscribe");
        return answer is null
            ? null
            : new PushDeviceRequest(
                answer.GetPropertyAsString("endpoint")!,
                answer.GetPropertyAsString("publicKey")!,
                answer.GetPropertyAsString("authSecret")!);
    }

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default) =>
        Call(UnsubscribeWith, "unsubscribe");

    #endregion

    #region Private Methods

    private async Task<T> Call<T>(Func<Task<T>> call, string function)
    {
        try
        {
            await (loaded ??= JSHost.ImportAsync(Module, ModulePath));
            return await call();
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The browser failed the notification call {Function}", function);
            throw new DeviceCallFailedException("The browser could not change notifications for this device.", exception);
        }
    }

    [JSImport("check", Module)]
    private static partial Task<JSObject> CheckWith();

    [JSImport("subscribe", Module)]
    private static partial Task<JSObject?> SubscribeWith(string publicKey);

    [JSImport("unsubscribe", Module)]
    private static partial Task<string?> UnsubscribeWith();

    #endregion
}
