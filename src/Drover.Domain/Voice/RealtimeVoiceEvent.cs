namespace Drover.Domain.Voice;

/// <summary>Something that happened on a live call, in Drover's own shape.</summary>
public abstract record RealtimeVoiceEvent;

/// <summary>A piece of the bot's spoken answer, as <see cref="RealtimeAudio"/> samples.</summary>
public sealed record RealtimeAudioOut(ReadOnlyMemory<byte> Samples) : RealtimeVoiceEvent;

/// <summary>The person started speaking. Audio still playing should stop: they are talking over the bot.</summary>
public sealed record RealtimePersonStartedSpeaking : RealtimeVoiceEvent;

/// <summary>The words of what the person just said.</summary>
public sealed record RealtimePersonSaid(string Text) : RealtimeVoiceEvent;

/// <summary>The next words of the answer being spoken.</summary>
public sealed record RealtimeBotSaying(string Text) : RealtimeVoiceEvent;

/// <summary>The whole answer, once the bot finished speaking it or was cut off.</summary>
public sealed record RealtimeBotSaid(string Text) : RealtimeVoiceEvent;

/// <summary>What one answer used, as the provider reported it.</summary>
public sealed record RealtimeTurnUsage(long InputTokens, long OutputTokens) : RealtimeVoiceEvent;

/// <summary>The provider reported a failure. The message is one of Drover's own sentences, never the provider's text.</summary>
public sealed record RealtimeFailed(ModelFailure Failure, string Message) : RealtimeVoiceEvent;
