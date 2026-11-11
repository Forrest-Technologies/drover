namespace Drover.Contracts.Secrets;

/// <summary>What is known about a stored secret. The value is never returned; <see cref="HasValue"/> says one exists.</summary>
public sealed record SecretSummary(
    Guid Id,
    string Kind,
    bool HasValue,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);
