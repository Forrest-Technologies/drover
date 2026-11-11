namespace Drover.Domain.Models;

/// <summary>A remaining balance a provider reported, for example "usd" or "diem".</summary>
public sealed record ModelBalance(string Currency, decimal Amount);
