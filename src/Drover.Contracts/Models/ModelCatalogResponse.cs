namespace Drover.Contracts.Models;

/// <summary>What is known about one model of a connection.</summary>
public sealed record ModelCatalogResponse(
    string Model,
    string DisplayName,
    ModelCapabilitiesResponse Capabilities,
    ModelPriceResponse Price,
    string? PrivacyMode,
    IReadOnlyList<string> Traits,
    string State,
    DateTimeOffset LastSeenAt);

public sealed record ModelCapabilitiesResponse(
    int? ContextSize,
    int? MaxOutputSize,
    bool ToolCalling,
    bool Streaming,
    bool Vision,
    bool Reasoning,
    IReadOnlyList<string> ReasoningEfforts,
    bool StructuredOutput,
    bool WebSearch);

/// <summary>Price per million tokens in <see cref="Currency"/>. A null price means unknown.</summary>
public sealed record ModelPriceResponse(decimal? InputPerMillion, decimal? OutputPerMillion, decimal? CachedInputPerMillion, string Currency);
