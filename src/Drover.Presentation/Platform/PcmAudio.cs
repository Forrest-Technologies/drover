namespace Drover.Presentation.Platform;

/// <summary>
/// The plain sound every device speaks: 16-bit little-endian samples, one channel, 24,000 a second. A live call
/// carries it both ways, a device without a decoder of its own asks the speech route for it, and a device without an
/// encoder of its own records it and wraps it as a WAV file, which the transcription route takes.
/// </summary>
public static class PcmAudio
{
    #region Properties

    public const int SampleRate = VoiceCallMessages.SampleRate;

    public const int BytesPerSample = VoiceCallMessages.BytesPerSample;

    /// <summary>The name the speech route gives this format.</summary>
    public const string Format = "pcm";

    public const string MediaType = "audio/pcm";

    public const string WavMediaType = "audio/wav";

    private const int WavHeaderBytes = 44;

    #endregion

    #region Public Methods

    /// <summary>How long a run of samples plays, in milliseconds.</summary>
    public static int Milliseconds(long bytes) => (int)(bytes / BytesPerSample * 1000 / SampleRate);

    /// <summary>The samples as a WAV file in memory: the same sound behind a 44 byte header.</summary>
    public static AudioClip ToWav(ReadOnlySpan<byte> samples)
    {
        var file = new byte[WavHeaderBytes + samples.Length];
        var header = file.AsSpan();
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], file.Length - 8);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], VoiceCallMessages.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], SampleRate * BytesPerSample * VoiceCallMessages.Channels);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], BytesPerSample * VoiceCallMessages.Channels);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], BytesPerSample * 8);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], samples.Length);
        samples.CopyTo(header[WavHeaderBytes..]);
        return new(file, WavMediaType);
    }

    #endregion
}
