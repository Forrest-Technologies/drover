namespace Drover.Contracts.Voice;

/// <summary>
/// What speech can use in a space: for each of listening, speaking and live calls, the models that can do it and the
/// one used now, and how much this server takes. A limit is null when the server sets none.
/// </summary>
public sealed record VoiceOptionsResponse(VoiceSlotResponse Listen, VoiceSlotResponse Speak, VoiceSlotResponse Live, VoiceLimits Limits);

/// <summary>The models that can fill one slot, and the one that does now. Null when nothing can.</summary>
public sealed record VoiceSlotResponse(IReadOnlyList<VoiceSourceResponse> Sources, VoiceSelection? Selected);

/// <summary>
/// One model that can fill a slot. <see cref="ConnectionId"/> is null, and <see cref="IsServer"/> true, for the
/// server's own speech service. <see cref="IsPrivateAddress"/> says the address reads as one on a private network.
/// </summary>
public sealed record VoiceSourceResponse(Guid? ConnectionId, string ConnectionName, string Model, bool IsServer, bool IsPrivateAddress);

/// <summary>The model a slot uses. <see cref="Automatic"/> is true when the server chose it because the space did not.</summary>
public sealed record VoiceSelection(
    Guid? ConnectionId,
    string ConnectionName,
    string Model,
    string? Voice,
    bool IsServer,
    bool IsPrivateAddress,
    bool Automatic);

public sealed record VoiceLimits(int? MaxRecordingBytes, int? MaxReadAloudCharacters, int? MaxCallMinutes);
