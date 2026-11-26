namespace Drover.Domain.Voice;

/// <summary>One piece of text to read aloud.</summary>
public sealed record SpeechRequest(string Text, string Model, string Voice, SpeechFormat Format)
{
    #region Properties

    /// <summary>How fast to speak, where 1 is the voice's own pace.</summary>
    public double Speed { get; init; } = 1;

    #endregion
}


/// <summary>The audio container a read-aloud answer arrives in.</summary>
public enum SpeechFormat
{
    /// <summary>Plays everywhere. The default.</summary>
    Mp3,

    Wav,

    Opus,

    /// <summary>Raw 16-bit little-endian samples, one channel, 24,000 a second. The lowest delay.</summary>
    Pcm,
}


/// <summary>A voice a person can pick: the name the provider takes, and the name to show.</summary>
public sealed record VoiceOption(string Id, string Name);


/// <summary>The wire names and media types of <see cref="SpeechFormat"/>.</summary>
public static class SpeechFormats
{
    #region Public Methods

    public static string WireName(this SpeechFormat format) =>
        format switch
        {
            SpeechFormat.Wav => "wav",
            SpeechFormat.Opus => "opus",
            SpeechFormat.Pcm => "pcm",
            _ => "mp3",
        };

    public static string MediaType(this SpeechFormat format) =>
        format switch
        {
            SpeechFormat.Wav => "audio/wav",
            SpeechFormat.Opus => "audio/ogg",
            SpeechFormat.Pcm => "audio/pcm",
            _ => "audio/mpeg",
        };

    public static SpeechFormat? Parse(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "mp3" => SpeechFormat.Mp3,
            "wav" => SpeechFormat.Wav,
            "opus" => SpeechFormat.Opus,
            "pcm" => SpeechFormat.Pcm,
            _ => null,
        };

    #endregion
}
