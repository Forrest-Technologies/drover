namespace Drover.App.Platforms.Android;

internal sealed class AndroidPushMessages(DroverApi api, AndroidPushRegistration registration, ILogger<AndroidPushMessages> logger)
{
    #region Public Methods

    public Task Show(AndroidPushSubscription saved, byte[] payload, CancellationToken cancellationToken) =>
        new PushWakeFlow(api, logger).Show(
            saved,
            saved.Open(payload),
            () => registration.BelongsTo(saved.UserId, saved.Token),
            new AndroidPushNotices(),
            cancellationToken);

    public async Task Reply(Intent intent, CancellationToken cancellationToken)
    {
        if (intent.Action != AndroidNotifications.ReplyAction || !AndroidNotifications.Conversation(intent, out var bot, out var thread))
        {
            return;
        }

        if (!Guid.TryParse(intent.GetStringExtra("user"), out var user)
            || !Guid.TryParse(intent.GetStringExtra("space"), out var space)
            || intent.GetStringExtra("registration") is not { } token
            || RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(AndroidNotifications.ReplyText)?.ToString()?.Trim() is not { Length: > 0 } text
            || !await registration.BelongsTo(user, token))
        {
            // Nothing is sent, and the notification goes, so Android does not show the reply as still sending.
            AndroidNotifications.Clear(thread);
            return;
        }

        var body = "Reply sent";
        try
        {
            if ((await api.GetSession(cancellationToken))?.UserId != user)
            {
                body = "Sign in to Drover to reply";
            }
            else
            {
                await api.SendMessage(space, thread, text, BusyModes.Queue, cancellationToken: cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or DroverApiException or OperationCanceledException)
        {
            logger.LogWarning("Drover could not confirm a notification reply");
            body = "Could not confirm your reply. Open Drover to check the conversation.";
        }

        if (await registration.BelongsTo(user, token))
        {
            AndroidNotifications.ShowReplyResult(bot, thread, intent.GetBooleanExtra("main", true), body, user, token, space);
        }
    }

    #endregion
}
