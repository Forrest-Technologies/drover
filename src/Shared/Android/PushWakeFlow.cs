namespace Drover.App.Platforms.Android;

/// <summary>What the device can show for a wake-up. The Android implementation posts system notifications.</summary>
internal interface IPushNotices
{
    #region Public Methods

    Task Show(PushWakeText wake, bool canReply);

    /// <summary>For a wake-up whose words could not be fetched: the person still learns that something
    /// happened.</summary>
    Task ShowChange();

    Task ShowTest();

    #endregion
}


/// <summary>
/// What the app does with a notification wake-up once the distributor has handed it over and its text is open: it is
/// the signed-in person's own, the server is asked for its words, and the device shows them. The wake-up is
/// acknowledged to the distributor before this runs, so it is never delivered again. Whatever goes wrong with either
/// request, whether it fails or runs out of the receiver's time, the person is still told that something changed:
/// losing it silently would look like notifications had stopped. Nothing is shown when the session answers with
/// another person or the registration is no longer this one.
/// </summary>
internal sealed class PushWakeFlow(DroverApi api, ILogger logger)
{
    #region Public Methods

    public async Task Show(
        AndroidPushSubscription saved,
        byte[] clear,
        Func<Task<bool>> belongsToThisDevice,
        IPushNotices notices,
        CancellationToken cancellationToken)
    {
        if (Read(clear, saved.UserId) is not { } wakeUp)
        {
            return;
        }

        try
        {
            if ((await api.GetSession(cancellationToken))?.UserId != saved.UserId || !await belongsToThisDevice())
            {
                return;
            }

            if (wakeUp.Kind == "device.test")
            {
                await notices.ShowTest();
                return;
            }

            if ((saved.Reported ?? saved.Endpoint) is not { } endpoint)
            {
                return;
            }

            var wake = await api.ReadPushWake(wakeUp.Kind, wakeUp.Thread, saved.UserId, endpoint, cancellationToken);
            if (await belongsToThisDevice())
            {
                if (wake is { } text && text.ThreadId == wakeUp.Thread)
                {
                    await notices.Show(text, canReply: wakeUp.Kind == "run.finished");
                }
                else
                {
                    await notices.ShowChange();
                }
            }
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            // The type only: nothing the exception says may carry a name or a word of what the person is told.
            logger.LogWarning(
                "Drover could not read what a notification is about; the failure was {Failure}",
                exception.GetType().Name);

            if (!WasRefused(exception) && await belongsToThisDevice())
            {
                await notices.ShowChange();
            }
        }
    }

    #endregion

    #region Private Methods

    private static bool IsReportable(Exception exception) =>
        exception is HttpRequestException or DroverApiException or OperationCanceledException;

    private static bool WasRefused(Exception exception) => exception switch
    {
        DroverApiException apiError => IsIdentityRefusal(apiError.StatusCode),
        HttpRequestException requestError => IsIdentityRefusal(requestError.StatusCode),
        _ => false,
    };

    private static bool IsIdentityRefusal(HttpStatusCode? status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict;

    private static (string Kind, Guid Thread)? Read(byte[] clear, Guid user)
    {
        using var document = JsonDocument.Parse(clear);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
            && kind.GetString() is ("device.test" or "run.finished" or "run.failed" or "approval.waiting") and var name
            && root.TryGetProperty("thread", out var thread) && thread.ValueKind == JsonValueKind.String
            && thread.TryGetGuid(out var id) && (id != Guid.Empty || name == "device.test")
            && (!root.TryGetProperty("user", out var intended)
                || (intended.ValueKind == JsonValueKind.String
                    && intended.TryGetGuid(out var parsed) && parsed == user)))
        {
            return (name, id);
        }

        return null;
    }

    #endregion
}
