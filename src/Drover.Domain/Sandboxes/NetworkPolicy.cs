namespace Drover.Domain.Sandboxes;

/// <summary>Outbound network for a sandbox. <paramref name="AllowedHosts"/> is read only for <see cref="NetworkAccess.Allowlist"/>.</summary>
public sealed record NetworkPolicy(NetworkAccess Access, IReadOnlyList<string> AllowedHosts);
