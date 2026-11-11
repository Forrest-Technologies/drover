namespace Drover.Domain.Models;

/// <summary>
/// What one model can do, in Drover's shape. Prices are dollars per million tokens and are null when unknown.
/// A provider that cannot say claims nothing, so a bot is never offered a feature the model lacks.
/// </summary>
public sealed record ModelCapabilities
{
    /// <summary>What a generic endpoint is assumed to do until the operator says more: tools, nothing else.</summary>
    public static ModelCapabilities Conservative { get; } = new() { ToolCalling = true };

    public int? ContextTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool ToolCalling { get; init; }

    public bool Vision { get; init; }

    public bool Reasoning { get; init; }

    /// <summary>The effort values the model accepts, in the provider's words. Empty when effort cannot be set.</summary>
    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];

    public bool StructuredOutput { get; init; }

    public bool WebSearch { get; init; }

    public decimal? InputPricePerMillion { get; init; }

    public decimal? OutputPricePerMillion { get; init; }

    public PrivacyMode Privacy { get; init; } = PrivacyMode.Unknown;
}
