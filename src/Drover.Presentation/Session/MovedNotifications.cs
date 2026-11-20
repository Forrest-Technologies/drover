namespace Drover.Ui.Session;

/// <summary>
/// Takes up again the notifications a browser had on at the client's former address, <c>/native/</c>. The server
/// retires the worker that held them (<c>retired-native-worker.js</c>), which ends its subscription and leaves a note
/// with the subscription's push service address. The first time a person is signed in after that, and only when the
/// browser still allows notifications for Drover, this subscribes the client at the root and asks the server to put
/// the new subscription in the place of the old one (<c>POST /api/push/device/move</c>). The server does that in one
/// request, for the one person it is signed in as, and only when the old subscription was theirs; so an account that
/// changed in another tab between the steps is never given someone else's notifications.
/// <para>
/// It never ends a subscription the client at the root already holds for another account on this browser: that
/// person is told how to choose to replace it. In every other case where nothing was taken up (the old subscription
/// was someone else's or the server no longer knows it, the browser no longer allows notifications, the server has no
/// key, or the browser made no subscription) a toast says notifications are off and where to turn them on. The note is
/// then forgotten. A server that cannot be reached, or a browser call that fails, keeps the note, and the next start
/// tries again.
/// </para>
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

    public static string HeldByAnother =>
        "Drover moved to a new address on this server. Notifications on this device are on for another account in this browser. To have them for yours instead, turn them on under Notifications in the account menu.";

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

            var outcome = moved is { State: MovedSubscription.PermissionKept, Endpoint: { } previous }
                && await WasThisPersons(previous, cancellationToken)
                ? await TakeUp(registration, previous, cancellationToken)
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
    /// Whether the server woke the signed-in person at the subscription the move ended. Only a shortcut, so the browser
    /// makes no subscription for someone the server will refuse: the server checks again when it takes the new one.
    /// </summary>
    private async Task<bool> WasThisPersons(string endpoint, CancellationToken cancellationToken) =>
        (await api.GetPushDeviceState(endpoint, cancellationToken)).On;

    /// <summary>
    /// Subscribes this client without asking (the browser already allows notifications) and has the server put the
    /// new subscription in the place of <paramref name="previous"/>. A subscription the client already holds is left
    /// alone: kept when it is this person's, and never ended when it is another account's.
    /// </summary>
    private async Task<TakeUpOutcome> TakeUp(IPushRegistration device, string previous, CancellationToken cancellationToken)
    {
        var check = await device.Check(cancellationToken);
        var publicKey = (await api.GetPushKey(cancellationToken)).PublicKey;

        if (check.Support is not DevicePushSupport.Available || publicKey is null)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        if (check.Endpoint is { } endpoint)
        {
            return (await api.GetPushDeviceState(endpoint, cancellationToken)).On
                ? TakeUpOutcome.AlreadyOn
                : TakeUpOutcome.HeldByAnother;
        }

        if (await device.Subscribe(publicKey, cancellationToken) is not { } subscription)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        if (await MoveOrEnd(device, previous, subscription, cancellationToken))
        {
            return TakeUpOutcome.TakenUp;
        }

        return TakeUpOutcome.NotTakenUp;
    }

    /// <summary>
    /// Has the server take the new subscription in the place of the old one. One the server refuses, or did not
    /// answer for, is ended at once, so the browser never holds a subscription that wakes nobody, and that a later
    /// start would take for another account's.
    /// </summary>
    private async Task<bool> MoveOrEnd(
        IPushRegistration device,
        string previous,
        PushDeviceRequest subscription,
        CancellationToken cancellationToken)
    {
        bool moved;

        try
        {
            moved = await api.MovePush(previous, subscription, cancellationToken);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            await device.Unsubscribe(cancellationToken);
            throw;
        }

        if (!moved)
        {
            await device.Unsubscribe(cancellationToken);
        }

        return moved;
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
            case TakeUpOutcome.HeldByAnother:
                toasts.Show(HeldByAnother, ToastKind.Warning);
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
        HeldByAnother,
        NotTakenUp,
    }

    #endregion
}
