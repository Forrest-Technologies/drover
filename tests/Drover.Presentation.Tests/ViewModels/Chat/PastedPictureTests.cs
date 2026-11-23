namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A picture pasted on a desktop comes from another program, so the native client bounds it before it is attached:
/// how many bytes are read, how many pictures it holds, and how many pixels it has.
/// </summary>
public sealed class PastedPictureTests
{
    #region Private Fields

    private const int Most = 100;

    #endregion

    #region Tests

    [Fact]
    public void The_most_read_from_the_clipboard_is_64_megabytes()
    {
        PastedPicture.Limit.Should().Be(64 * 1024 * 1024);
    }

    [Fact]
    public async Task A_stream_that_holds_more_than_it_said_is_refused_without_reading_past_the_limit()
    {
        var source = new MeasuredStream(1000);

        var read = await PastedPicture.Read(source, 10, Most, CancellationToken.None);

        read.Should().BeNull("the stream held more than the limit, whatever size it said it had");
        source.LargestAsk.Should().BeLessThanOrEqualTo(Most, "no read asks for more than is left of the limit");
        source.Asked.Should().BeLessThanOrEqualTo(Most + 1, "one byte past the limit is all it takes to tell");
    }

    [Fact]
    public async Task A_stream_that_holds_less_than_it_said_is_read_whole()
    {
        var source = new MeasuredStream(20);

        var read = await PastedPicture.Read(source, 90, Most, CancellationToken.None);

        read.Should().Equal(MeasuredStream.Bytes(20));
    }

    [Fact]
    public async Task A_stream_of_exactly_the_limit_with_no_size_said_is_taken()
    {
        var source = new MeasuredStream(Most);

        var read = await PastedPicture.Read(source, 0, Most, CancellationToken.None);

        read.Should().Equal(MeasuredStream.Bytes(Most));
        source.Asked.Should().BeLessThanOrEqualTo(Most + 1);
    }

    [Fact]
    public async Task A_stream_that_says_it_is_past_the_limit_is_not_read_at_all()
    {
        var source = new MeasuredStream(Most + 1);

        var read = await PastedPicture.Read(source, Most + 1, Most, CancellationToken.None);

        read.Should().BeNull();
        source.Asked.Should().Be(0);
    }

    [Fact]
    public void A_complete_png_is_kept_as_it_is()
    {
        var png = Picture(SKEncodedImageFormat.Png, 2, 3);

        PastedPicture.ToPng(png).Should().BeSameAs(png);
    }

    [Fact]
    public void A_picture_in_another_format_is_written_again_as_png()
    {
        var jpeg = Picture(SKEncodedImageFormat.Jpeg, 2, 3);

        var png = PastedPicture.ToPng(jpeg);

        png.Should().NotBeNull();
        using var codec = SKCodec.Create(new MemoryStream(png!));
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Png);
        (codec.Info.Width, codec.Info.Height).Should().Be((2, 3));
    }

    [Fact]
    public void A_moving_picture_is_refused()
    {
        PastedPicture.ToPng(TwoFrameGif()).Should().BeNull("only one still picture is attached from the clipboard");
    }

    [Fact]
    public void A_picture_with_more_pixels_than_the_limit_is_refused_before_it_is_decoded()
    {
        PastedPicture.ToPng(Sized(Picture(SKEncodedImageFormat.Png, 1, 1), 8000, 4001)).Should().BeNull();
    }

    [Fact]
    public void A_picture_of_exactly_the_most_pixels_is_taken()
    {
        var png = Sized(Picture(SKEncodedImageFormat.Png, 1, 1), 8000, 4000);

        PastedPicture.ToPng(png).Should().BeSameAs(png);
    }

    [Fact]
    public void Bytes_that_are_not_a_picture_are_refused()
    {
        PastedPicture.ToPng(Encoding.UTF8.GetBytes("Sample words, not a picture")).Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static byte[] Picture(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    // The same PNG with its header saying another size, so a codec reads those dimensions without any pixels drawn.
    private static byte[] Sized(byte[] png, int width, int height)
    {
        var sized = png.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(sized.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(sized.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(sized.AsSpan(29), Crc(sized.AsSpan(12, 17)));
        return sized;
    }

    // A one-pixel GIF with two frames, written by hand: Skia encodes no GIF.
    private static byte[] TwoFrameGif()
    {
        byte[] header = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF];
        byte[] frame = [0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0];
        return [.. header, .. frame, .. frame, 0x3B];
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) is 1 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    #endregion

    #region Nested Types

    // A stream of a given number of bytes that records how much each read asked for, whatever size it is said to be.
    private sealed class MeasuredStream(int holds) : Stream
    {
        #region Private Fields

        private int position;

        #endregion

        #region Properties

        public long Asked { get; private set; }

        public int LargestAsk { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public static byte[] Bytes(int count) => [.. Enumerable.Range(0, count).Select(index => (byte)(index % 251))];

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            Asked += buffer.Length;
            LargestAsk = Math.Max(LargestAsk, buffer.Length);
            var count = Math.Min(buffer.Length, holds - position);

            for (var index = 0; index < count; index++)
            {
                buffer[index] = (byte)((position + index) % 251);
            }

            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
