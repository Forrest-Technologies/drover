namespace Drover.App.Platforms.Android;

internal sealed class AndroidPushMessages(DroverApi api, AndroidPushRegistration registration, ILogger<AndroidPushMessages> logger)
{
    #region Public Methods

    public async Task Show(AndroidPushSubscription saved, byte[] payload, CancellationToken cancellationToken)
    {
        var (kind, thread) = Read(saved.Open(payload));
        if (kind.Length == 0 || (await api.GetSession(cancellationToken))?.UserId != saved.UserId
            || !await registration.BelongsTo(saved.UserId, saved.Token))
        {
            return;
        }

        if (kind == "device.test")
        {
            AndroidNotifications.ShowTest();
        }
        else if (await api.ReadPushWake(kind, thread, cancellationToken) is { } wake
            && await registration.BelongsTo(saved.UserId, saved.Token))
        {
            AndroidNotifications.Show(wake, saved, canReply: kind == "run.finished");
        }
    }

    public async Task Reply(Intent intent, CancellationToken cancellationToken)
    {
        if (intent.Action != AndroidNotifications.ReplyAction || !AndroidNotifications.Conversation(intent, out var bot, out var thread)
            || !Guid.TryParse(intent.GetStringExtra("user"), out var user)
            || !Guid.TryParse(intent.GetStringExtra("space"), out var space)
            || intent.GetStringExtra("registration") is not { } token
            || RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(AndroidNotifications.ReplyText)?.ToString()?.Trim() is not { Length: > 0 } text
            || !await registration.BelongsTo(user, token))
        {
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
            AndroidNotifications.ShowReplyResult(bot, thread, intent.GetBooleanExtra("main", true), body);
        }
    }

    #endregion

    #region Private Methods

    private static (string Kind, Guid Thread) Read(byte[] clear)
    {
        using var document = JsonDocument.Parse(clear);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
            && kind.GetString() is ("device.test" or "run.finished" or "run.failed" or "approval.waiting") and var name
            && root.TryGetProperty("thread", out var thread) && thread.ValueKind == JsonValueKind.String
            && thread.TryGetGuid(out var id) && (id != Guid.Empty || name == "device.test"))
        {
            return (name, id);
        }

        return (string.Empty, Guid.Empty);
    }

    #endregion
}
