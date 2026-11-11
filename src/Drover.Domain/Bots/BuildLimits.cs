namespace Drover.Domain.Bots;

/// <summary>Narrows the space's caps for one bot. A null limit leaves the space's cap in force.</summary>
public sealed record BuildLimits
{
    public int? MaxChildren { get; init; }

    public int? MaxDepthBelow { get; init; }
}
