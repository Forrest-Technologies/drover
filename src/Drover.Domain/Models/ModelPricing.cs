namespace Drover.Domain.Models;

/// <summary>Price per million tokens, in <see cref="Currency"/>. A null price means unknown.</summary>
public sealed record ModelPricing
{
    #region Properties

    public decimal? InputPerMillion { get; init; }

    public decimal? OutputPerMillion { get; init; }

    public decimal? CachedInputPerMillion { get; init; }

    public string Currency { get; init; } = "USD";

    #endregion
}
