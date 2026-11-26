namespace Drover.Engine.Voice;

/// <summary>
/// Joins the WAV answers to the pieces of one text into one WAV stream, as they arrive. The first answer's 44-byte
/// header is kept, with its two lengths set to "unknown" because the total is not known while streaming; the header
/// of every later answer is dropped, so only its samples follow. Every piece must come from the same model and
/// voice, so the sample format in the first header holds for all of them.
/// </summary>
public sealed class WavJoin
{
    #region Private Fields

    public const int HeaderBytes = 44;

    private const int RiffLengthOffset = 4;

    private const int DataLengthOffset = 40;

    private readonly byte[] header = new byte[HeaderBytes];

    private int headerFilled;

    private int pieces;

    #endregion

    #region Public Methods

    /// <summary>Says the next chunk begins a new answer, whose header is to be taken off.</summary>
    public void StartPiece()
    {
        pieces++;
        headerFilled = 0;
    }

    /// <summary>What of <paramref name="chunk"/> belongs in the joined stream. Empty while a header is still arriving.</summary>
    public ReadOnlyMemory<byte> Take(ReadOnlyMemory<byte> chunk)
    {
        if (headerFilled == HeaderBytes)
        {
            return chunk;
        }

        var taken = Math.Min(HeaderBytes - headerFilled, chunk.Length);
        chunk[..taken].CopyTo(header.AsMemory(headerFilled));
        headerFilled += taken;
        var samples = chunk[taken..];

        if (headerFilled < HeaderBytes || pieces > 1)
        {
            return headerFilled < HeaderBytes ? ReadOnlyMemory<byte>.Empty : samples;
        }

        var joined = new byte[HeaderBytes + samples.Length];
        header.CopyTo(joined, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(joined.AsSpan(RiffLengthOffset), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(joined.AsSpan(DataLengthOffset), uint.MaxValue);
        samples.CopyTo(joined.AsMemory(HeaderBytes));
        return joined;
    }

    #endregion
}
