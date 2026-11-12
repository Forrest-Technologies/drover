namespace Drover.Domain.Policy;

/// <summary>When subscription sign-in is offered for a model provider. Ordered from strictest to loosest.</summary>
public enum SubscriptionSignInMode
{
    OnlyWithOperatorApproval,
    WhereProviderPermits,
}
