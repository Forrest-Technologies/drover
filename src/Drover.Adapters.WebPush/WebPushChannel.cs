namespace Drover.Adapters.WebPush;

/// <summary>
/// <see cref="IPushChannel"/> over Web Push: the wake-up, encrypted to the device, posted to its push service and
/// signed with the server's VAPID key. Serves browsers, the installed web app on every platform, and UnifiedPush.
/// The payload is the kind and the thread, and no header names either.
/// </summary>
public sealed class WebPushChannel(HttpClient http, WebPushKeys keys, ILogger<WebPushChannel> logger) : IPushChannel
{
    #region Private Fields

    /// <summary>A wake-up a day old is no longer worth waking a device for.</summary>
    private const int TimeToLiveSeconds = 24 * 60 * 60;

    #endregion

    #region Properties

    public string Kind => "web-push";

    #endregion

    #region Interface Implementations

    public async Task<PushOutcome> Send(DeviceSubscription subscription, PushWakeUp wakeUp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey) { Subject = keys.Subject };
        var client = new PushServiceClient(http) { AutoRetryAfter = false, DefaultTimeToLive = TimeToLiveSeconds };

        try
        {
            await client.RequestPushMessageDeliveryAsync(
                ToServiceSubscription(subscription),
                ToMessage(wakeUp),
                authentication,
                VapidAuthenticationScheme.Vapid,
                cancellationToken);
            return PushOutcome.Delivered;
        }
        catch (PushServiceClientException refused)
        {
            return OutcomeFor(refused.StatusCode);
        }
        catch (Exception unusable) when (unusable is CryptographicException or FormatException or ArgumentException)
        {
            logger.LogWarning(unusable, "Subscription {SubscriptionId} has keys a payload cannot be encrypted to", subscription.Id);
            return PushOutcome.SubscriptionGone;
        }
        catch (HttpRequestException blocked) when (IsBlocked(blocked))
        {
            logger.LogWarning(blocked, "Subscription {SubscriptionId} points at an address this server does not reach", subscription.Id);
            return PushOutcome.SubscriptionGone;
        }
        catch (HttpRequestException unreachable)
        {
            logger.LogWarning(unreachable, "The push service for subscription {SubscriptionId} could not be reached", subscription.Id);
            return PushOutcome.RetryLater;
        }
        catch (TaskCanceledException timedOut) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(timedOut, "The push service for subscription {SubscriptionId} did not answer in time", subscription.Id);
            return PushOutcome.RetryLater;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Gone and not found are the push service's words for a device that is no more; a bad request means the
    /// subscription itself cannot be used. Anything else may pass, so it is tried again.
    /// </summary>
    private PushOutcome OutcomeFor(HttpStatusCode status)
    {
        if (status is HttpStatusCode.Gone or HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return PushOutcome.SubscriptionGone;
        }

        logger.LogWarning("The push service answered {Status}; the wake-up will be tried again", (int)status);
        return PushOutcome.RetryLater;
    }

    /// <summary>The outbound policy refused the address: it will never be reached, so retrying only repeats the refusal.</summary>
    private static bool IsBlocked(HttpRequestException exception) =>
        exception is OutboundBlockedException || exception.InnerException is OutboundBlockedException;

    private static ServiceSubscription ToServiceSubscription(DeviceSubscription subscription)
    {
        var target = new ServiceSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.PublicKey);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.AuthSecret);
        return target;
    }

    // Every wake-up is one the person sees at once, so each is sent as urgent. A phone in deep idle holds anything less
    // until its next maintenance window, which can be hours away.
    private static PushMessage ToMessage(PushWakeUp wakeUp) =>
        new(JsonSerializer.Serialize(new { kind = wakeUp.Kind, thread = wakeUp.ThreadId }))
        {
            Urgency = PushMessageUrgency.High,
        };

    #endregion
}
