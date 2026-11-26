namespace Drover.Presentation.Platform;

/// <summary>
/// Plays a clip the server sends, as it arrives. Every device asks for the same format, a WAV file, because its
/// header says how fast to play it and what follows is plain samples, which every device plays without a decoder of
/// its own. Nothing of the clip is kept once it has been played.
/// </summary>
public static class ClipPlayback
{
    #region Private Fields

    private const int ReadBytes = 16 * 1024;

    // The fixed part of a WAV file's format: kind, channels, rate, bytes a second, bytes a frame, bits a sample.
    private const int FormatBytes = 16;

    private const int PlainSamples = 1;

    private const int ExtensibleFormat = 0xFFFE;

    #endregion

    #region Properties

    /// <summary>The format to ask the speech route for.</summary>
    public const string Format = "wav";

    #endregion

    #region Public Methods

    /// <summary>
    /// Plays the clip in <paramref name="audio"/> and returns when it has been played to its end. Cancelling stops
    /// it at once. A clip that is not 16-bit samples in a WAV file, or plain samples, throws
    /// <see cref="DeviceCallFailedException"/>.
    /// </summary>
    public static async Task PlayClip(this IAudioPlayback playback, Stream audio, string mediaType, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBytes];
        var shape = mediaType.StartsWith(PcmAudio.MediaType, StringComparison.OrdinalIgnoreCase)
            ? new ClipShape(PcmAudio.SampleRate, PcmAudio.Channels)
            : await ReadWavHeader(audio, buffer, cancellationToken);

        await using var output = await playback.Open(shape.SampleRate, cancellationToken);
        var played = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var whole = false;

        void Drained()
        {
            if (Volatile.Read(ref whole))
            {
                played.TrySetResult();
            }
        }

        output.Drained += Drained;

        try
        {
            await Pump(audio, output, shape.Channels, buffer, cancellationToken);
            Volatile.Write(ref whole, true);

            if (output.IsPlaying)
            {
                await played.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            output.Drained -= Drained;
            output.Flush();
        }
    }

    #endregion

    #region Private Methods

    // Whole frames only: a sample, or a frame of several channels, cut in two by a read is finished by the next.
    private static async Task Pump(Stream audio, IAudioOutput output, int channels, byte[] buffer, CancellationToken cancellationToken)
    {
        var frame = PcmAudio.BytesPerSample * channels;
        var kept = 0;

        while (true)
        {
            var read = await audio.ReadAsync(buffer.AsMemory(kept), cancellationToken);

            if (read == 0)
            {
                return;
            }

            var held = kept + read;
            var usable = held - (held % frame);

            if (usable > 0)
            {
                output.Queue(ToOneChannel(buffer.AsSpan(0, usable), channels));
            }

            kept = held - usable;
            buffer.AsSpan(usable, kept).CopyTo(buffer);
        }
    }

    // The output plays one channel. More are averaged into it.
    private static byte[] ToOneChannel(ReadOnlySpan<byte> frames, int channels)
    {
        if (channels == 1)
        {
            return frames.ToArray();
        }

        var one = new byte[frames.Length / channels];

        for (var at = 0; at < one.Length; at += PcmAudio.BytesPerSample)
        {
            var sum = 0;

            for (var channel = 0; channel < channels; channel++)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(frames[((at * channels) + (channel * PcmAudio.BytesPerSample))..]);
            }

            BinaryPrimitives.WriteInt16LittleEndian(one.AsSpan(at), (short)(sum / channels));
        }

        return one;
    }

    // Reads up to where the samples begin. The sizes a streamed file states for itself are not trusted: the samples
    // run to the end of the stream.
    private static async Task<ClipShape> ReadWavHeader(Stream audio, byte[] buffer, CancellationToken cancellationToken)
    {
        await Fill(audio, buffer.AsMemory(0, 12), cancellationToken);

        if (!buffer.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !buffer.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new DeviceCallFailedException("The clip is not a WAV file.");
        }

        ClipShape? shape = null;

        while (true)
        {
            await Fill(audio, buffer.AsMemory(0, 8), cancellationToken);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));

            if (buffer.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                return shape ?? throw new DeviceCallFailedException("The clip does not say how to play it.");
            }

            if (size > buffer.Length)
            {
                throw new DeviceCallFailedException("The clip's header is not one this device reads.");
            }

            var isFormat = buffer.AsSpan(0, 4).SequenceEqual("fmt "u8);
            await Fill(audio, buffer.AsMemory(0, (int)(size + (size & 1))), cancellationToken);

            if (isFormat)
            {
                shape = ReadFormat(buffer.AsSpan(0, (int)size));
            }
        }
    }

    private static ClipShape ReadFormat(ReadOnlySpan<byte> format)
    {
        if (format.Length < FormatBytes || BinaryPrimitives.ReadUInt16LittleEndian(format) is not (PlainSamples or ExtensibleFormat))
        {
            throw new DeviceCallFailedException("The clip is not plain sound this device plays.");
        }

        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);

        return bits == PcmAudio.BytesPerSample * 8 && channels is > 0 and <= 8 && rate is >= 8000 and <= 96_000
            ? new(rate, channels)
            : throw new DeviceCallFailedException("The clip is not 16-bit sound this device plays.");
    }

    private static async Task Fill(Stream audio, Memory<byte> wanted, CancellationToken cancellationToken)
    {
        try
        {
            await audio.ReadExactlyAsync(wanted, cancellationToken);
        }
        catch (EndOfStreamException cutShort)
        {
            throw new DeviceCallFailedException("The clip ended before its sound began.", cutShort);
        }
    }

    #endregion

    #region Nested Types

    private readonly record struct ClipShape(int SampleRate, int Channels);

    #endregion
}
