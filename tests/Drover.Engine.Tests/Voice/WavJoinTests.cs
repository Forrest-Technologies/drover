namespace Drover.Engine.Tests.Voice;

public sealed class WavJoinTests
{
    #region Joining

    [Fact]
    public void The_first_header_is_kept_with_unknown_lengths_and_later_headers_are_dropped()
    {
        var join = new WavJoin();
        List<byte> joined = [];

        foreach (var piece in new[] { Wav([1, 2, 3, 4]), Wav([5, 6]), Wav([7, 8, 9]) })
        {
            join.StartPiece();
            joined.AddRange(join.Take(piece).ToArray());
        }

        joined.Should().HaveCount(WavJoin.HeaderBytes + 9);
        Encoding.ASCII.GetString([.. joined[..4]]).Should().Be("RIFF");
        BinaryPrimitives.ReadUInt32LittleEndian([.. joined[4..8]]).Should().Be(uint.MaxValue);
        Encoding.ASCII.GetString([.. joined[36..40]]).Should().Be("data");
        BinaryPrimitives.ReadUInt32LittleEndian([.. joined[40..44]]).Should().Be(uint.MaxValue);
        joined[8..36].Should().Equal(Wav([])[8..36], "the sample format of the first answer stands");
        joined[WavJoin.HeaderBytes..].Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(43)]
    [InlineData(44)]
    [InlineData(45)]
    public void A_header_that_arrives_in_parts_is_still_taken_whole(int chunkBytes)
    {
        var join = new WavJoin();
        List<byte> joined = [];

        foreach (var piece in new[] { Wav([10, 11, 12]), Wav([13, 14, 15, 16]) })
        {
            join.StartPiece();

            foreach (var chunk in piece.Chunk(chunkBytes))
            {
                joined.AddRange(join.Take(chunk).ToArray());
            }
        }

        joined.Should().HaveCount(WavJoin.HeaderBytes + 7);
        joined[WavJoin.HeaderBytes..].Should().Equal(10, 11, 12, 13, 14, 15, 16);
    }

    #endregion

    #region Private Methods

    /// <summary>A canonical 44-byte header for 16-bit mono at 24,000 samples a second, then the samples.</summary>
    private static byte[] Wav(byte[] samples)
    {
        var wav = new byte[WavJoin.HeaderBytes + samples.Length];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(36 + samples.Length));
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), 24_000);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), 48_000);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)samples.Length);
        samples.CopyTo(wav, WavJoin.HeaderBytes);
        return wav;
    }

    #endregion
}
