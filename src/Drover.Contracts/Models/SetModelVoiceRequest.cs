namespace Drover.Contracts.Models;

/// <summary>
/// What a space admin says a model of a compatible connection does with speech. All three false clears the mark,
/// and the model goes back to what its name implies at the next refresh.
/// </summary>
public sealed record SetModelVoiceRequest(string? Model, bool SpeechToText, bool TextToSpeech, bool RealtimeVoice);
