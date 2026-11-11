namespace Drover.Domain.Models;

/// <summary>One model a connection offers. Traits are the provider's own picks, such as "default" or "default_code".</summary>
public sealed record ModelInfo(
    string Id,
    string DisplayName,
    ModelCapabilities Capabilities,
    IReadOnlyList<string> Traits);
