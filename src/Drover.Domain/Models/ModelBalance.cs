namespace Drover.Domain.Models;

/// <summary>A provider balance snapshot, for example "usd" or "diem". Venice reports the balance before the request.</summary>
public sealed record ModelBalance(string Currency, decimal Amount);
