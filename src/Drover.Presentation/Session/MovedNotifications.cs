namespace Drover.Ui.Session;

/// <summary>
/// Takes up again the notifications a browser had on at the client's former address, <c>/native/</c>. The server
/// retires the worker that held them (<c>retired-native-worker.js</c>), which ends its subscription and leaves a note
/// with the subscription's push service address. The first time a person is signed in after that, this asks the
/// server whether that address was that person's. Only then, and only when the browser still allows notifications for
/// Drover, does it subscribe the client at the root in its place without asking. In every other case (the
/// subscription was someone else's or the server no longer knows it, the browser no longer allows notifications, the
/// server has no key, or the browser made no subscription) a toast says notifications are off and where to turn them
/// on. The note is then forgotten. A server that cannot be reached, or a browser call that fails, keeps the note, and
/// the next start tries again.
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

            if (moved.State is MovedSubscription.None)
            {
                return;
            }

            var outcome = moved.State is MovedSubscription.PermissionKept && await WasThisPersons(moved.Endpoint, cancellationToken)
                ? await TakeUp(registration, cancellationToken)
                : TakeUpOutcome.NotTakenUp;

            Tell(outcome);
            await registration.ForgetMovedSubscription(cancellationToken);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Notifications from the former address were not taken up; the next start tries again");
            tried = false;
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The browser failed while notifications from the former address were taken up; the next start tries again");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Whether the server woke the signed-in person at the subscription the move ended. A subscription another
    /// account on this browser held is never taken up for this one.
    /// </summary>
    private async Task<bool> WasThisPersons(string? endpoint, CancellationToken cancellationToken) =>
        endpoint is not null && (await api.GetPushDeviceState(endpoint, cancellationToken)).On;

    /// <summary>
    /// Subscribes this client for the person, as turning notifications on does, without asking: the browser already
    /// allows them. The person already on at the root keeps that subscription.
    /// </summary>
    private async Task<TakeUpOutcome> TakeUp(IPushRegistration device, CancellationToken cancellationToken)
    {
        var check = await device.Check(cancellationToken);
        var publicKey = (await api.GetPushKey(cancellationToken)).PublicKey;

        if (check.Support is not DevicePushSupport.Available || publicKey is null)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        if (check.Endpoint is { } endpoint)
        {
            if ((await api.GetPushDeviceState(endpoint, cancellationToken)).On)
            {
                return TakeUpOutcome.AlreadyOn;
            }

            // Someone else's subscription on this device, as turning notifications on finds it: ended first.
            await device.Unsubscribe(cancellationToken);
        }

        if (await device.Subscribe(publicKey, cancellationToken) is not { } subscription)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        await api.TurnOnPush(subscription, cancellationToken);
        return TakeUpOutcome.TakenUp;
    }

    private void Tell(TakeUpOutcome outcome)
    {
        switch (outcome)
        {
            case TakeUpOutcome.TakenUp:
                toasts.Show(Resumed);
                break;
            case TakeUpOutcome.NotTakenUp:
                toasts.Show(TurnOnAgain, ToastKind.Warning);
                break;
            case TakeUpOutcome.AlreadyOn:
                break;
        }
    }

    #endregion

    #region Nested Types

    private enum TakeUpOutcome
    {
        TakenUp,
        AlreadyOn,
        NotTakenUp,
    }

    #endregion
}
