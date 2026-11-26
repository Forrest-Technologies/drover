namespace Drover.Contracts.Models;

/// <summary>
/// A connection as the space sees it. <see cref="HasKey"/> says a key is stored; the key is never returned.
/// <see cref="SignIn"/> is null for a key connection, and for one each person signs in to it is the caller's own
/// sign-in only.
/// </summary>
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
    DateTimeOffset? LastCheckedAt,
    ModelSignInSummary? SignIn = null);

/// <summary>A balance the provider reported, for example "usd" or "diem". A snapshot from before the request.</summary>
public sealed record BalanceAmount(string Currency, decimal Amount);
