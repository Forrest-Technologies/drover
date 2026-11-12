namespace Drover.Domain.Models;

/// <summary>What a provider reported about the account's balances the last time it answered.</summary>
public sealed record ConnectionBalance
{
    public IReadOnlyList<ModelBalance> Amounts { get; init; } = [];

    public DateTimeOffset? ReadAt { get; init; }
}
