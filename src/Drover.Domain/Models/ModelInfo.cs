namespace Drover.Domain.Models;

/// <summary>A discovered model, with capabilities, pricing and privacy in Drover's own shape.</summary>
public sealed record ModelInfo(string Id, string DisplayName, ModelCapabilities Capabilities, IReadOnlyList<string> Traits)
{
    public ModelPricing Pricing { get; init; } = new();

    public PrivacyMode Privacy { get; init; } = PrivacyMode.Unknown;
}
