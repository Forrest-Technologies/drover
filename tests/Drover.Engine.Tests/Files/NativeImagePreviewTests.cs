namespace Drover.Engine.Tests.Files;

public sealed class NativeImagePreviewTests
{
    #region Private Fields

    private const string ProgressiveJpeg = """
        /9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYn
        KSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgo
        KCgoKCgoKCj/wgARCAAwAEADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/9oA
        DAMBAAIQAxAAAAGUgAAAAAAAAAAA/8QAFBABAAAAAAAAAAAAAAAAAAAAUP/aAAgBAQABBQJD/8QAFBEBAAAAAAAAAAAAAAAAAAAA
        MP/aAAgBAwEBPwFP/8QAFBEBAAAAAAAAAAAAAAAAAAAAMP/aAAgBAgEBPwFP/8QAFBABAAAAAAAAAAAAAAAAAAAAUP/aAAgBAQAG
        PwJD/8QAFBABAAAAAAAAAAAAAAAAAAAAUP/aAAgBAQABPyFD/9oADAMBAAIAAwAAABDzzzzzzzzzzzz/xAAUEQEAAAAAAAAAAAAA
        AAAAAAAw/9oACAEDAQE/EE//xAAUEQEAAAAAAAAAAAAAAAAAAAAw/9oACAECAQE/EE//xAAUEAEAAAAAAAAAAAAAAAAAAABQ/9oA
        CAEBAAE/EEP/2Q==
        """;

    private const string MultiScanBaselineJpeg = """
        /9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAx
        NDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy
        MjIyMjIyMjL/wAARCAAwAEADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUF
        BAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVW
        V1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi
        4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiv/8QAHwEAAwEB
        AQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMz
        UvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaX
        mJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oACgICEQMRAD8AAAAAAAAA
        AAAAAAAA/9k=
        """;

    private const string SinglePixelGif = "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==";

    #endregion

    #region Tests

    [Fact]
    public void An_ordinary_baseline_picture_is_reduced_within_the_native_caps()
    {
        var source = Picture(1600, 960, SKEncodedImageFormat.Jpeg);

        var preview = NativeImagePreview.Resize(source);

        preview.Refusal.Should().BeNull();
        preview.Image.Should().NotBeNull().And.HaveCountLessThanOrEqualTo(NativeFilePreview.MaxImageBytes);
        Dimensions(preview.Image!).Should().Be((480, 288));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void A_normal_picture_without_scanline_support_still_gets_a_preview(SKEncodedImageFormat format)
    {
        var source = Picture(640, 480, format);

        var preview = NativeImagePreview.Resize(source);

        preview.Refusal.Should().BeNull();
        Dimensions(preview.Image!).Should().Be((480, 360));
    }

    [Fact]
    public void A_normal_gif_without_scanline_support_still_gets_a_preview()
    {
        var source = Convert.FromBase64String(SinglePixelGif);

        var preview = NativeImagePreview.Resize(source);

        preview.Refusal.Should().BeNull();
        Dimensions(preview.Image!).Should().Be((1, 1));
    }

    [Fact]
    public void An_ordinary_progressive_picture_stays_within_the_normal_scan_budget()
    {
        var source = Convert.FromBase64String(ProgressiveJpeg);

        var preview = NativeImagePreview.Resize(source);

        preview.Refusal.Should().BeNull();
        Dimensions(preview.Image!).Should().Be((64, 48));
    }

    [Fact]
    public void A_benign_progressive_picture_is_refused_when_its_injected_scan_budget_is_smaller()
    {
        var source = Convert.FromBase64String(ProgressiveJpeg);
        var limits = NativeImagePreview.DefaultLimits with { MaxJpegScans = 1 };

        var preview = NativeImagePreview.Resize(source, limits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
    }

    [Fact]
    public void A_progressive_picture_must_fit_the_source_budget_even_if_the_preview_is_small()
    {
        var source = Convert.FromBase64String(ProgressiveJpeg);
        var limits = NativeImagePreview.DefaultLimits with { MaxMultiScanPixels = 64 * 48 - 1 };

        var preview = NativeImagePreview.Resize(source, limits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        preview.Image.Should().BeNull();
    }

    [Fact]
    public void A_multi_scan_baseline_picture_must_fit_the_multi_scan_budget()
    {
        var source = Convert.FromBase64String(MultiScanBaselineJpeg);
        var limits = NativeImagePreview.DefaultLimits with { MaxMultiScanPixels = 64 * 48 - 1 };

        var preview = NativeImagePreview.Resize(source, limits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        preview.Image.Should().BeNull();
    }

    [Fact]
    public void A_normal_multi_scan_baseline_picture_stays_within_the_default_budget()
    {
        var source = Convert.FromBase64String(MultiScanBaselineJpeg);

        var preview = NativeImagePreview.Resize(source);

        preview.Refusal.Should().BeNull();
        Dimensions(preview.Image!).Should().Be((64, 48));
    }

    [Fact]
    public void A_partial_first_scan_applies_the_multi_scan_budget_before_a_second_scan()
    {
        var source = Convert.FromBase64String(MultiScanBaselineJpeg);
        byte[] scanMarker = [0xFF, 0xDA];
        var firstScan = source.AsSpan().IndexOf(scanMarker);
        var nextScan = source.AsSpan((firstScan + 2)..).IndexOf(scanMarker);
        var beforeSecondScan = source[..(firstScan + 2 + nextScan)];
        var limits = NativeImagePreview.DefaultLimits with { MaxMultiScanPixels = 64 * 48 - 1 };

        var preview = NativeImagePreview.Resize(beforeSecondScan, limits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        preview.Image.Should().BeNull();
    }

    [Fact]
    public void Compressed_png_metadata_is_removed_before_the_codec_without_changing_retained_chunks()
    {
        var source = WithPngMetadata(Picture(8, 8, SKEncodedImageFormat.Png));
        byte[]? codecInput = null;

        var preview = NativeImagePreview.Resize(
            source,
            NativeImagePreview.DefaultLimits,
            TimeProvider.System,
            input => codecInput = input.ToArray());

        preview.Refusal.Should().BeNull();
        codecInput.Should().NotBeNull();
        var expected = PngChunks(source)
            .Where(chunk => chunk.Type is not ("iCCP" or "zTXt" or "iTXt"))
            .ToArray();
        var actual = PngChunks(codecInput!).ToArray();
        actual.Select(chunk => chunk.Type).Should().Equal(expected.Select(chunk => chunk.Type));
        foreach (var (expectedChunk, actualChunk) in expected.Zip(actual))
        {
            actualChunk.Bytes.Should().Equal(expectedChunk.Bytes);
            actualChunk.Crc.Should().Be(expectedChunk.Crc);
        }
    }

    [Fact]
    public void A_png_metadata_chunk_whose_length_runs_past_the_input_is_refused()
    {
        var source = WithPngMetadata(Picture(8, 8, SKEncodedImageFormat.Png));
        var chunkType = source.AsSpan().IndexOf("iCCP"u8);
        var truncated = source[..(chunkType + 5)];

        var preview = NativeImagePreview.Resize(truncated);

        preview.Refusal.Should().Be(NativePreviewRefusal.Malformed);
        preview.Image.Should().BeNull();
    }

    [Fact]
    public void A_scanline_decode_stops_at_its_independent_deadline()
    {
        var source = Picture(1600, 960, SKEncodedImageFormat.Jpeg);
        var limits = NativeImagePreview.DefaultLimits with { Deadline = TimeSpan.FromMilliseconds(500) };

        var preview = NativeImagePreview.Resize(source, limits, new SteppingTimeProvider());

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
    }

    [Fact]
    public void A_normal_picture_is_refused_if_the_codec_disagrees_with_the_header_reader()
    {
        var png = Picture(640, 480, SKEncodedImageFormat.Png);

        var preview = NativeImagePreview.ResizeFromHeader(
            png, 480, 640, NativeImagePreview.DefaultLimits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        preview.Image.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static byte[] Picture(int width, int height, SKEncodedImageFormat format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(format, quality: 90);
        return encoded.ToArray();
    }

    private static (int Width, int Height) Dimensions(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return (codec.Info.Width, codec.Info.Height);
    }

    private static byte[] WithPngMetadata(byte[] png)
    {
        var compressed = Compress("small metadata"u8);
        byte[][] added =
        [
            BuildPngChunk("iCCP", [.. "profile"u8, 0, 0, .. compressed]),
            BuildPngChunk("zTXt", [.. "note"u8, 0, 0, .. compressed]),
            BuildPngChunk("iTXt", [.. "note"u8, 0, 1, 0, 0, 0, .. compressed]),
            BuildPngChunk("tEXt", [.. "safe"u8, 0, .. "retained"u8]),
        ];
        var firstLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4));
        var afterHeader = checked(8 + 12 + firstLength);
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, afterHeader));
        foreach (var chunk in added)
        {
            output.Write(chunk);
        }

        output.Write(png.AsSpan(afterHeader));
        return output.ToArray();
    }

    private static byte[] Compress(ReadOnlySpan<byte> bytes)
    {
        using var output = new MemoryStream();
        using (var compressed = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressed.Write(bytes);
        }

        return output.ToArray();
    }

    private static byte[] BuildPngChunk(string type, ReadOnlySpan<byte> data)
    {
        var kind = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        kind.CopyTo(chunk, 4);
        data.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), PngCrc(kind, data));
        return chunk;
    }

    private static IEnumerable<PngChunk> PngChunks(byte[] png)
    {
        var position = 8;
        while (position <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            var bytes = png.AsSpan(position, checked(length + 12)).ToArray();
            var type = Encoding.ASCII.GetString(png, position + 4, 4);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length - 4));
            yield return new(type, bytes, crc);
            position += bytes.Length;
            if (type == "IEND")
            {
                yield break;
            }
        }
    }

    private static uint PngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = PngCrcByte(crc, value);
        }

        foreach (var value in data)
        {
            crc = PngCrcByte(crc, value);
        }

        return ~crc;
    }

    private static uint PngCrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320U : 0);
        }

        return crc;
    }

    #endregion

    #region Test Doubles

    private sealed class SteppingTimeProvider : TimeProvider
    {
        #region Private Fields

        private long timestamp;

        #endregion

        #region Properties

        public override long TimestampFrequency => 1000;

        #endregion

        #region Public Methods

        public override long GetTimestamp() => Interlocked.Add(ref timestamp, 100);

        #endregion
    }

    #endregion

    #region Nested Types

    private sealed record PngChunk(string Type, byte[] Bytes, uint Crc);

    #endregion
}
