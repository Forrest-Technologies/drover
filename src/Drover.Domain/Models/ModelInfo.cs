namespace Drover.Domain.Models;

/// <summary>A discovered model, with capabilities, pricing and privacy in Drover's own shape.</summary>
public sealed record ModelInfo(string Id, string DisplayName, ModelCapabilities Capabilities, IReadOnlyList<string> Traits)
{
    public ModelPricing Pricing { get; init; } = new();

    public PrivacyMode Privacy { get; init; } = PrivacyMode.Unknown;

    /// <summary>
    /// False when the provider could have said more about this model and did not get to: a refresh then keeps the
    /// capabilities and prices already stored for it instead of replacing them with what the list alone gave.
    /// </summary>
    public bool FactsRead { get; init; } = true;
}
