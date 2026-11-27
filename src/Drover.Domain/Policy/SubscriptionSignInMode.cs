namespace Drover.Domain.Policy;

/// <summary>When subscription sign-in is offered for a model provider. Ordered from strictest to loosest.</summary>
public enum SubscriptionSignInMode
{
    /// <summary>A personal plan kind is refused; a sign-in the provider offers to any app stays on.</summary>
    OnlyWithOperatorApproval,

    /// <summary>Every sign-in kind this server has is on, with nothing to set.</summary>
    Available,
}
