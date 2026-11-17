namespace Drover.Domain.Sandboxes;

/// <summary>The provider's own record of a sandbox, as opposed to Drover's row.</summary>
public sealed record SandboxInfo(
    ProviderSandboxState State,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset? EndsAt);
