namespace Drover.Domain.Policy;

/// <summary>How per-space quotas are set. Ordered from strictest to loosest.</summary>
public enum QuotaMode
{
    SetByOperator,
    Unlimited,
}
