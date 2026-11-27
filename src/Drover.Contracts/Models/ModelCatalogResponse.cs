namespace Drover.Contracts.Models;

/// <summary>
/// What is known about one model of a connection. <see cref="Capabilities"/> and <see cref="Price"/> carry the
/// effective values: an admin's where one is set, the provider's where not. <see cref="OperatorSet"/> names the
/// members the admin set, using the member names of the two records in camel case (<c>contextSize</c>,
/// <c>vision</c>, <c>inputPerMillion</c>), so a screen can mark them.
/// </summary>
public sealed record ModelCatalogResponse(
    string Model,
    string DisplayName,
    ModelCapabilitiesResponse Capabilities,
    ModelPriceResponse Price,
    string? PrivacyMode,
    IReadOnlyList<string> Traits,
    string State,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<string>? OperatorSet = null);

public sealed record ModelCapabilitiesResponse(
    int? ContextSize,
    int? MaxOutputSize,
    bool ToolCalling,
    bool Streaming,
    bool Vision,
    bool Reasoning,
    IReadOnlyList<string> ReasoningEfforts,
    bool StructuredOutput,
    bool WebSearch,
    bool ImageGeneration = false,
    bool VisionSetByOperator = false,
    bool SpeechToText = false,
    bool TextToSpeech = false,
    bool RealtimeVoice = false,
    bool VoiceSetByOperator = false,
    bool PdfInput = false);

/// <summary>Price per million tokens in <see cref="Currency"/>. A null price means unknown.</summary>
public sealed record ModelPriceResponse(decimal? InputPerMillion, decimal? OutputPerMillion, decimal? CachedInputPerMillion, string Currency);
