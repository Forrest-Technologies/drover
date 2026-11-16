namespace Drover.Domain.Push;

public enum PushOutcome
{
    Delivered,
    SubscriptionGone,
    RetryLater,
}
