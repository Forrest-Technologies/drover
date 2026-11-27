namespace Drover.Domain.Models;

/// <summary>What is known about one model of one connection, in Drover's own shape.</summary>
public sealed class ModelCatalogEntry : ISpaceOwned
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid ModelConnectionId { get; set; }

    public required string Model { get; set; }

    public required string DisplayName { get; set; }

    public ModelCapabilities Capabilities { get; set; } = new();

    public ModelPricing Pricing { get; set; } = new();

    /// <summary>
    /// What a space admin set, over what the provider reported. A refresh never touches it. Null when nothing is set.
    /// </summary>
    public ModelCapabilityOverrides? OperatorCapabilities { get; set; }

    /// <summary>Prices a space admin typed, over what the provider reported. Null when nothing is set.</summary>
    public ModelPricing? OperatorPricing { get; set; }

    /// <summary>The provider's own term: private, anonymized, attested.</summary>
    public string? PrivacyMode { get; set; }

    public string[] Traits { get; set; } = [];

    public AvailabilityState State { get; set; } = AvailabilityState.Available;

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// What the model can do: the admin's value where one is set, the provider's where not. Every reader outside
    /// the catalog refresh uses this, never <see cref="Capabilities"/>.
    /// </summary>
    public ModelCapabilities EffectiveCapabilities => OperatorCapabilities?.ApplyTo(Capabilities) ?? Capabilities;

    /// <summary>
    /// The price per million tokens, price by price: the admin's where set, the provider's where not. The admin's
    /// currency applies to the prices the admin set. A price the provider reported in another currency is not mixed
    /// in: it becomes unknown, which a cost limit treats as unknown and not as free.
    /// </summary>
    public ModelPricing EffectivePricing =>
        OperatorPricing is { } set
            ? new ModelPricing
            {
                InputPerMillion = set.InputPerMillion ?? ProviderPrice(set, Pricing.InputPerMillion),
                OutputPerMillion = set.OutputPerMillion ?? ProviderPrice(set, Pricing.OutputPerMillion),
                CachedInputPerMillion = set.CachedInputPerMillion ?? ProviderPrice(set, Pricing.CachedInputPerMillion),
                Currency = set.Currency,
            }
            : Pricing;

    #endregion

    #region Private Methods

    private decimal? ProviderPrice(ModelPricing set, decimal? price) =>
        string.Equals(set.Currency, Pricing.Currency, StringComparison.OrdinalIgnoreCase) ? price : null;

    #endregion
}
