namespace Drover.Domain.Models;

/// <summary>What a provider reported about the account's balance the last time it answered.</summary>
public sealed record ConnectionBalance
{
    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? ReadAt { get; init; }
}
