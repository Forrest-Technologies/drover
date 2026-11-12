namespace Drover.Contracts.Models;

/// <summary>A connection as the space sees it. <see cref="HasKey"/> says a key is stored; the key is never returned.</summary>
public sealed record ModelConnectionSummary(
    Guid Id,
    string Name,
    string Kind,
    string? BaseUrl,
    bool HasKey,
    string Status,
    string? DefaultModel,
    IReadOnlyList<BalanceAmount> Balances,
    DateTimeOffset? BalanceReadAt,
    DateTimeOffset? LastCheckedAt);

/// <summary>A balance the provider reported, for example "usd" or "diem". A snapshot from before the request.</summary>
public sealed record BalanceAmount(string Currency, decimal Amount);
