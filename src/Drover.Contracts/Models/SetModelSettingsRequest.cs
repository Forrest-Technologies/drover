namespace Drover.Contracts.Models;

/// <summary>
/// Input only: what a space admin says one model can do and costs. The model is named in the body because a routed
/// model's ID holds slashes. Every member left out or null clears that one setting, so the provider's own value is
/// in effect for it: the request is the whole set of what the admin says, not a change to it.
/// </summary>
public sealed record SetModelSettingsRequest(string? Model, ModelAbilitiesRequest? Capabilities, ModelPricingRequest? Pricing);

/// <summary>The abilities an admin can set. A null member is not set.</summary>
public sealed record ModelAbilitiesRequest(
    int? ContextSize = null,
    int? MaxOutputSize = null,
    bool? ToolCalling = null,
    bool? Vision = null,
    bool? Reasoning = null,
    IReadOnlyList<string>? ReasoningEfforts = null,
    bool? StructuredOutput = null,
    bool? ImageGeneration = null);

/// <summary>Prices an admin can set, per million tokens in <see cref="Currency"/>. A null price is not set.</summary>
public sealed record ModelPricingRequest(
    decimal? InputPerMillion = null,
    decimal? OutputPerMillion = null,
    decimal? CachedInputPerMillion = null,
    string? Currency = null);
