namespace Drover.Domain.Models;

/// <summary>Transport destination only. Persistent connection rows and credentials remain separate.</summary>
public sealed record ModelEndpoint(string Kind, Uri BaseAddress)
{
    /// <summary>What model discovery accepts from this endpoint.</summary>
    public DiscoveryLimits Limits { get; init; } = DiscoveryLimits.Default;

    /// <summary>
    /// Models of this connection whose facts are already stored. A provider that must ask for the facts of each model
    /// one by one asks for the others first, so a pass that runs out of time still moves on.
    /// </summary>
    public IReadOnlySet<string> Filled { get; init; } = new HashSet<string>();
}
