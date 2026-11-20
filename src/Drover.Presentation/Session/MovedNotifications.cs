namespace Drover.Ui.Session;

/// <summary>
/// Takes up again the notifications a browser had on at the client's former address, <c>/native/</c>. The server
/// retires the worker that held them (<c>retired-native-worker.js</c>), which ends its subscription and leaves a note
/// that it did. The first time a person is signed in after that, this subscribes the client at the root in its place,
/// when the browser still allows notifications for Drover: the browser asks nothing, because permission was given
/// before. When it no longer allows them, a toast says notifications are off and where to turn them on again. Either
/// way the note is forgotten, unless the server could not be reached, and then the next start tries again.
/// </summary>
public sealed class MovedNotifications(
    DroverApi api,
    ToastService toasts,
    ILogger<MovedNotifications> logger,
    IPushRegistration? registration = null)
{
    #region Private Fields

    private bool tried;

    #endregion

    #region Properties

    public static string Resumed =>
        "Drover moved to a new address on this server. Notifications on this device moved with it.";

    public static string TurnOnAgain =>
        "Drover moved to a new address on this server, which turned notifications off on this device. Turn them on again under Notifications in the account menu.";

    #endregion

    #region Public Methods

    /// <summary>Runs once per start of the app, for a signed-in person.</summary>
    public async Task Resume(CancellationToken cancellationToken = default)
    {
        if (tried || registration is null)
        {
            return;
        }

        tried = true;

        try
        {
            var moved = await registration.FindMovedSubscription(cancellationToken);

            if (moved is MovedSubscription.None)
            {
                return;
            }

            if (moved is MovedSubscription.PermissionKept && await TakeUp(registration, cancellationToken))
            {
                toasts.Show(Resumed);
            }
            else if (moved is MovedSubscription.PermissionLost)
            {
                toasts.Show(TurnOnAgain, ToastKind.Warning);
            }

            await registration.ForgetMovedSubscription(cancellationToken);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Notifications from the former address were not taken up; the next start tries again");
            tried = false;
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The browser failed while notifications from the former address were taken up");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Subscribes this client for the person, as turning notifications on does, without asking: the browser already
    /// allows them. True when a new subscription was made; false when one was already on for this person, or the
    /// server has no key to subscribe with.
    /// </summary>
    private async Task<bool> TakeUp(IPushRegistration device, CancellationToken cancellationToken)
    {
        var check = await device.Check(cancellationToken);
        var publicKey = (await api.GetPushKey(cancellationToken)).PublicKey;

        if (check.Support is not DevicePushSupport.Available || publicKey is null)
        {
            return false;
        }

        if (check.Endpoint is { } endpoint)
        {
            if ((await api.GetPushDeviceState(endpoint, cancellationToken)).On)
            {
                return false;
            }

            // Someone else's subscription on this device, as turning notifications on finds it: ended first.
            await device.Unsubscribe(cancellationToken);
        }

        if (await device.Subscribe(publicKey, cancellationToken) is not { } subscription)
        {
            return false;
        }

        await api.TurnOnPush(subscription, cancellationToken);
        return true;
    }

    #endregion
}
