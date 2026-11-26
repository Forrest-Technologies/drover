namespace Drover.Engine.Voice;

/// <summary>Why a voice request was not carried out. Each kind is one status for the API to answer with.</summary>
public enum VoiceProblemKind
{
    /// <summary>The request itself is wrong.</summary>
    Invalid,

    /// <summary>What was named is not there for this person.</summary>
    NotFound,

    /// <summary>More than this server takes.</summary>
    TooLarge,

    /// <summary>Nothing in the space, or on the server, can do this yet, or the conversation cannot take it now.</summary>
    NotReady,

    /// <summary>The speech provider failed or refused.</summary>
    Provider,
}


/// <summary>A sentence for the person who asked, and what kind of refusal it is.</summary>
public sealed record VoiceProblem(VoiceProblemKind Kind, string Message);

/// <summary>What a voice request came to: its value, or the reason there is none.</summary>
public sealed record VoiceOutcome<T>(T? Value, VoiceProblem? Problem)
{
    public static implicit operator VoiceOutcome<T>(VoiceProblem problem) => new(default, problem);
}

/// <summary>What one slot can use and what it uses now.</summary>
public sealed record VoiceSlotView(IReadOnlyList<VoiceCandidate> Candidates, VoicePick? Pick);

/// <summary>Everything the voice settings screen and the composer need to know about speech in a space.</summary>
public sealed record VoiceOptionsView(
    VoiceSlotView Listen,
    VoiceSlotView Speak,
    VoiceSlotView Live,
    int MaxRecordingBytes,
    int MaxReadAloudCharacters,
    int MaxCallMinutes);

/// <summary>What recording a call's exchange came to.</summary>
public enum CallWrite
{
    Written,

    /// <summary>A run began working on the conversation, so nothing was written and the call must end.</summary>
    RunActive,

    /// <summary>The conversation is no longer there for this person.</summary>
    Gone,
}


/// <summary>
/// One read-aloud answer, ready to be asked for. A class and not a record on purpose: it holds the connection's
/// key, and a record would print it.
/// </summary>
public sealed class SpeechPlan
{
    #region Properties

    /// <summary>The media type of the audio that will be streamed.</summary>
    public required string MediaType { get; init; }

    public required SpeechFormat Format { get; init; }

    public required IReadOnlyList<string> Pieces { get; init; }

    public required string Model { get; init; }

    public required string Voice { get; init; }

    public Guid? ConnectionId { get; init; }

    internal required ITextToSpeech Adapter { get; init; }

    internal required ModelEndpoint Endpoint { get; init; }

    internal string? Key { get; init; }

    internal required EffectivePolicy Policy { get; init; }

    #endregion
}


/// <summary>One live call, ready to be opened. A class for the same reason as <see cref="SpeechPlan"/>.</summary>
public sealed class CallPlan
{
    #region Properties

    public required Guid SpaceId { get; init; }

    public required Guid UserId { get; init; }

    public required Guid ThreadId { get; init; }

    public required Guid BotId { get; init; }

    public required string Model { get; init; }

    public required string Voice { get; init; }

    /// <summary>Null when the server sets no limit on a call.</summary>
    public int? MaxMinutes { get; init; }

    public Guid? ConnectionId { get; init; }

    internal required IRealtimeVoice Adapter { get; init; }

    internal required ModelEndpoint Endpoint { get; init; }

    internal string? Key { get; init; }

    internal required EffectivePolicy Policy { get; init; }

    internal required RealtimeVoiceOptions Options { get; init; }

    #endregion
}
