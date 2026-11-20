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
/// <para>
/// The new subscription is kept with the note before the server is asked. When the server's answer is lost, nothing
/// is ended: the next start asks the server whose that subscription is, keeps it if the move was taken, and ends it
/// and tries again if not. A subscription is only ever ended while it is still the one this attempt made.
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

            var outcome = moved.State is MovedSubscription.PermissionKept
                ? await TakeUp(registration, moved, cancellationToken)
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
    /// Settles what an earlier attempt left, then subscribes this client without asking (the browser already allows
    /// notifications) and has the server put the new subscription in the place of the moved one.
    /// <list type="bullet">
    /// <item>A subscription the client holds that is this person's is kept. It is the one an earlier attempt made when
    /// the server took it but its answer was lost; otherwise the person had already turned notifications on.</item>
    /// <item>The earlier attempt's own subscription, which the server did not take, is ended, and the attempt is made
    /// again.</item>
    /// <item>Any other subscription is another account's, and is never ended.</item>
    /// </list>
    /// </summary>
    private async Task<TakeUpOutcome> TakeUp(IPushRegistration device, MovedPush moved, CancellationToken cancellationToken)
    {
        var check = await device.Check(cancellationToken);

        if (check.Endpoint is { } current)
        {
            if ((await api.GetPushDeviceState(current, cancellationToken)).On)
            {
                return current == moved.Candidate ? TakeUpOutcome.TakenUp : TakeUpOutcome.AlreadyOn;
            }

            if (current != moved.Candidate)
            {
                return TakeUpOutcome.HeldByAnother;
            }

            await device.UnsubscribeOnly(current, cancellationToken);
        }

        var publicKey = moved.Endpoint is { } previous && await WasThisPersons(previous, cancellationToken)
            ? (await api.GetPushKey(cancellationToken)).PublicKey
            : null;

        if (check.Support is not DevicePushSupport.Available || publicKey is null)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        if (await device.Subscribe(publicKey, cancellationToken) is not { } subscription)
        {
            return TakeUpOutcome.NotTakenUp;
        }

        return await Move(device, moved.Endpoint!, subscription, cancellationToken);
    }

    /// <summary>
    /// Has the server take the new subscription in the place of the old one. The new one is kept with the note first,
    /// so when the server's answer is lost the subscription stays, and the next start asks whose it is before it ends
    /// anything. One the server refused is ended, and only while it is still the device's: never one another account
    /// made in the meantime.
    /// </summary>
    private async Task<TakeUpOutcome> Move(
        IPushRegistration device,
        string previous,
        PushDeviceRequest subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            await device.RememberMoveCandidate(subscription.Endpoint, cancellationToken);
        }
        catch (DeviceCallFailedException)
        {
            // Not kept, so a later start could not tell it is this attempt's: it is ended now instead.
            await device.UnsubscribeOnly(subscription.Endpoint, cancellationToken);
            throw;
        }

        if (await api.MovePush(previous, subscription, cancellationToken))
        {
            return TakeUpOutcome.TakenUp;
        }

        await device.UnsubscribeOnly(subscription.Endpoint, cancellationToken);
        return TakeUpOutcome.NotTakenUp;
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
