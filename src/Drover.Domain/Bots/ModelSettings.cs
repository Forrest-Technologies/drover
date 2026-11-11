namespace Drover.Domain.Bots;

/// <summary>Per-bot model options, in Drover's neutral names. An adapter maps them to its provider's.</summary>
public sealed record ModelSettings
{
    public double? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public string? ReasoningEffort { get; init; }

    public WebSearchMode WebSearch { get; init; } = WebSearchMode.Off;
}
