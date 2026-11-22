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
        var limits = NativeImagePreview.DefaultLimits with { MaxProgressivePixels = 64 * 48 - 1 };

        var preview = NativeImagePreview.Resize(source, limits, TimeProvider.System);

        preview.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
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
}
