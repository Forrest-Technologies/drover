namespace Drover.Domain.Policy;

/// <summary>Which sandbox adapters a deployment offers. Ordered from strictest to loosest.</summary>
public enum SandboxAdapterSet
{
    HardBoundaryOnly,
    All,
}
