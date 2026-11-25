namespace Drover.App.Platforms.Android;

internal sealed class AndroidPushMessages(
    DroverApi api, AndroidPushRegistration registration, ILogger<AndroidPushMessages> logger)
{
    #region Public Methods

    public Task Show(AndroidPushSubscription saved, byte[] payload, CancellationToken cancellationToken) =>
        new PushWakeFlow(api, logger).Show(
            saved,
            saved.Open(payload),
            () => registration.BelongsTo(saved.UserId, saved.Token),
            new AndroidPushNotices(registration, saved),
            cancellationToken);

    public async Task Reply(Intent intent, CancellationToken cancellationToken)
    {
        if (intent.Action != AndroidNotifications.ReplyAction
            || !OperatingSystem.IsAndroidVersionAtLeast(31)
            || AndroidApplication.Context.GetSystemService(Context.KeyguardService) is not KeyguardManager keyguard
            || keyguard.IsDeviceLocked
            || AndroidNotifications.ReadTarget(intent) is not { Space: { } space } target
            || RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(AndroidNotifications.ReplyText)?.ToString()
                is not { } draft
            || string.IsNullOrWhiteSpace(draft)
            || !await registration.BelongsTo(target.User, target.Registration))
        {
            return;
        }

        MessageResponse? sent = null;
        try
        {
            if ((await api.GetSession(cancellationToken))?.UserId != target.User)
            {
                await registration.WhileRegistered(target.User, target.Registration,
                    () => AndroidNotifications.Dismiss(target));
                return;
            }

            if (!await registration.BelongsTo(target.User, target.Registration) || keyguard.IsDeviceLocked)
            {
                return;
            }

            var response = await api.SendMessage(space, target.Thread, draft.Trim(), BusyModes.Queue,
                cancellationToken: cancellationToken);
            sent = response.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or DroverApiException or OperationCanceledException)
        {
            logger.LogWarning("Drover could not confirm a notification reply");
        }

        await registration.WhileRegistered(target.User, target.Registration,
            () => AndroidNotifications.ShowReplyResult(target, draft, sent));
    }

    #endregion
}
