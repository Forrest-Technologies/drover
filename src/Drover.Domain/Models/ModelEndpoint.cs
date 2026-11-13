namespace Drover.Domain.Models;

/// <summary>Transport destination only. Persistent connection rows and credentials remain separate.</summary>
public sealed record ModelEndpoint(string Kind, Uri BaseAddress)
{
    /// <summary>What model discovery accepts from this endpoint.</summary>
    public DiscoveryLimits Limits { get; init; } = DiscoveryLimits.Default;
}
