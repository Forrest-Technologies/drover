namespace Drover.Domain.Models;

/// <summary>What is known about one model of one connection, in Drover's own shape.</summary>
public sealed class ModelCatalogEntry : ISpaceOwned
{
    public Guid SpaceId { get; set; }

    public Guid ModelConnectionId { get; set; }

    public required string Model { get; set; }

    public required string DisplayName { get; set; }

    public ModelCapabilities Capabilities { get; set; } = new();

    public ModelPricing Pricing { get; set; } = new();

    /// <summary>The provider's own term: private, anonymized, attested.</summary>
    public string? PrivacyMode { get; set; }

    public string[] Traits { get; set; } = [];

    public AvailabilityState State { get; set; } = AvailabilityState.Available;

    public DateTimeOffset LastSeenAt { get; set; }
}
