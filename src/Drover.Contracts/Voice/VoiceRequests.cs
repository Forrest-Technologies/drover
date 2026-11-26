namespace Drover.Contracts.Voice;

/// <summary>The space's choices. A slot left null is chosen by the server.</summary>
public sealed record VoiceSettingsRequest(VoiceChoiceBody? Listen, VoiceChoiceBody? Speak, VoiceChoiceBody? Live);

/// <summary>One model of one connection of the space, and the voice it speaks in. A null voice is the model's first.</summary>
public sealed record VoiceChoiceBody(Guid ConnectionId, string? Model, string? Voice = null);

/// <summary>A voice a person can pick: the name the provider takes, and the name to show.</summary>
public sealed record VoiceOptionResponse(string Id, string Name);

/// <summary>The words of a recording.</summary>
public sealed record TranscriptResponse(string Text);

/// <summary>
/// What to read aloud: a message by its id, or text (for a sample of a voice). Exactly one of the two.
/// <see cref="Format"/> is mp3 (the default), wav, opus or pcm; the answer's content type says what was sent.
/// </summary>
public sealed record SpeakRequest(Guid? MessageId = null, string? Text = null, string? Voice = null, string? Format = null);
