namespace Drover.Domain.Models;

/// <summary>
/// How much of a provider's model list Drover accepts. A hostile or broken endpoint can answer with an endless
/// body or millions of models, so both are capped; the deployment policy sets the values.
/// </summary>
public sealed record DiscoveryLimits(int MaxBytes, int MaxModels)
{
    /// <summary>The self-hosted values, used where a caller states none.</summary>
    public static DiscoveryLimits Default { get; } = new(8 * 1024 * 1024, 5000);
}
