namespace Drover.Domain.Push;

/// <summary>
/// Delivers a wake-up to one device. The wake-up is the only content: the device then asks the person's own server,
/// with its own credentials, what to show. A channel never sees message text, bot names, or titles.
/// </summary>
public interface IPushChannel
{
    /// <summary>The channel's name, such as <c>web-push</c>, for logs and the status page.</summary>
    string Kind { get; }

    /// <summary>
    /// Sends the wake-up. A push service that will never deliver to the subscription again answers
    /// <see cref="PushOutcome.SubscriptionGone"/>; one that is busy, down, or unreachable answers
    /// <see cref="PushOutcome.RetryLater"/>. Cancellation throws; it is never reported as an outcome.
    /// </summary>
    Task<PushOutcome> Send(PushSubscription subscription, PushWakeUp wakeUp, CancellationToken cancellationToken);
}
