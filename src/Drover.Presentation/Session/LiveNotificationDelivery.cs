namespace Drover.Ui.Session;

/// <summary>
/// Turns one live signal into a system notification. The desktop app follows the stream and asks this to show
/// the ones that qualify, so a test can see the notification without a window.
/// </summary>
public static class LiveNotificationDelivery
{
    #region Public Methods

    public static async Task Deliver(
        DroverApi api,
        Navigator navigator,
        Action<string, string, long> show,
        ILogger logger,
        Guid spaceId,
        long version,
        UserSignal signal,
        Func<bool> isCurrent,
        Func<bool> appActive,
        CancellationToken cancellationToken)
    {
        if (LiveNotificationDecision.Kind(signal) is not { } kind || signal.ThreadId is not { } threadId)
        {
            return;
        }

        try
        {
            var wake = await api.ReadPushWake(kind, threadId, cancellationToken);
            if (!isCurrent() || wake is null || wake.SpaceId is { } wakeSpaceId && wakeSpaceId != spaceId)
            {
                return;
            }

            var choice = await api.GetBotNotify(spaceId, wake.BotId, cancellationToken);
            if (isCurrent()
                && LiveNotificationDecision.ShouldShow(wake, choice.Notifies, navigator.Current, appActive()))
            {
                show(NotificationText.Sanitize(wake.Body), LiveNotificationDecision.Address(wake), version);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped preparing a desktop notification for thread {ThreadId}", threadId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(
                exception,
                "A desktop notification for thread {ThreadId} could not be prepared",
                threadId);
        }
    }

    #endregion
}
