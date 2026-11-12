namespace Drover.Domain.Policy;

/// <summary>Which model providers a deployment offers. Ordered from strictest to loosest.</summary>
public enum ModelProviderSet
{
    OperatorList,
    All,
}
