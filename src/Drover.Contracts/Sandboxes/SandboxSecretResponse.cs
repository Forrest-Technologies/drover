namespace Drover.Contracts.Sandboxes;

/// <summary>A bot a sandbox secret is for.</summary>
public sealed record SandboxSecretBotRef(Guid Id, string Name);

/// <summary>
/// What may be shown about a sandbox secret. The value is never returned, nor its length or anything derived
/// from it: after it is saved a person sees only its name, where it may be sent, who may use it, and when it was
/// last used and replaced.
/// </summary>
public sealed record SandboxSecretResponse(
    Guid Id,
    string Name,
    string Description,
    string Mode,
    IReadOnlyList<string> Hosts,
    bool AllowInQuery,
    bool AllowInBody,
    string Scope,
    IReadOnlyList<SandboxSecretBotRef> Bots,
    Guid? ProjectId,
    string? ProjectName,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? LastUsedAt);
