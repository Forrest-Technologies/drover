namespace Drover.Engine.Files;

/// <summary>Makes a bounded, static PNG for the native client from a stored picture.</summary>
internal static class NativeImagePreview
{
    #region Private Fields

    private const int MaxInputBytes = DeploymentPolicy.SelfHostedMaxFileBytes;

    private const int MaxSourceDimension = 32_768;

    private const long MaxSourcePixels = 50_000_000;

    private const long MaxDecodedPixels = 16_000_000;

    private const int MaxPreviewEdge = 480;

    private static readonly SKSamplingOptions sampling = new(SKCubicResampler.Mitchell);

    #endregion

    #region Public Methods

    public static NativeFilePreview Resize(byte[] bytes)
    {
        if (bytes.Length > MaxInputBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        return ResizeImage(bytes);
    }

    #endregion

    #region Private Methods

    private static NativeFilePreview ResizeImage(byte[] bytes)
    {
        if (!FileInspector.TryReadImageDimensions(bytes, out var width, out var height))
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        if (!WithinSourceBounds(width, height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        ArrayBufferWriter<byte>? stripped = null;
        if (IsPng(bytes) && !TryStripPngMetadata(bytes, out stripped))
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        using var data = stripped is null
            ? SKData.CreateCopy(bytes)
            : SKData.CreateCopy(stripped.WrittenSpan);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg
            or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Webp))
        {
            return new(Refusal: NativePreviewRefusal.Unsupported);
        }

        var source = codec.Info;
        if (source.Width != width || source.Height != height || !WithinSourceBounds(source.Width, source.Height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var decodeSize = codec.GetScaledDimensions(ScaleFor(source.Width, source.Height));
        if (!WithinDecodedBounds(decodeSize.Width, decodeSize.Height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var decodeInfo = new SKImageInfo(
            decodeSize.Width,
            decodeSize.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var decoded = new SKBitmap();
        if (!decoded.TryAllocPixels(decodeInfo)
            || codec.GetPixels(decodeInfo, decoded.GetPixels()) is not SKCodecResult.Success)
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        return Encode(decoded, codec.EncodedOrigin);
    }

    private static NativeFilePreview Encode(SKBitmap decoded, SKEncodedOrigin origin)
    {
        var fitted = Fit(decoded.Width, decoded.Height);
        var swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = swapsSides
            ? new SKImageInfo(fitted.Height, fitted.Width, SKColorType.Bgra8888, SKAlphaType.Premul)
            : new SKImageInfo(fitted.Width, fitted.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(output);
        if (surface is null)
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        surface.Canvas.Clear(SKColors.Transparent);
        Orient(surface.Canvas, origin, fitted.Width, fitted.Height);
        surface.Canvas.DrawBitmap(decoded, SKRect.Create(fitted.Width, fitted.Height), sampling);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        if (encoded is null || encoded.Size > NativeFilePreview.MaxImageBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        return new(Image: encoded.ToArray());
    }

    private static bool WithinSourceBounds(int width, int height) =>
        width is > 0 and <= MaxSourceDimension
        && height is > 0 and <= MaxSourceDimension
        && (long)width * height <= MaxSourcePixels;

    private static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..];

    // These chunks may contain deflate streams unrelated to the bounded pixel buffer. A preview does not need them.
    // The buffer is allocated lazily, so an ordinary PNG makes no second managed copy.
    private static bool TryStripPngMetadata(
        ReadOnlySpan<byte> bytes,
        out ArrayBufferWriter<byte>? stripped)
    {
        stripped = null;
        var position = 8;
        while (position <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[position..]);
            if (length > int.MaxValue || length > (uint)(bytes.Length - position - 12))
            {
                return false;
            }

            var type = bytes.Slice(position + 4, 4);
            var chunkLength = checked((int)length + 12);
            var compressedMetadata = type.SequenceEqual("iCCP"u8)
                || type.SequenceEqual("zTXt"u8)
                || type.SequenceEqual("iTXt"u8);
            if (compressedMetadata)
            {
                if (stripped is null)
                {
                    stripped = new(bytes.Length);
                    stripped.Write(bytes[..position]);
                }
            }
            else
            {
                stripped?.Write(bytes.Slice(position, chunkLength));
            }

            position += chunkLength;
            if (type.SequenceEqual("IEND"u8))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WithinDecodedBounds(int width, int height) =>
        width is > 0 and <= MaxSourceDimension
        && height is > 0 and <= MaxSourceDimension
        && (long)width * height <= MaxDecodedPixels;

    private static float ScaleFor(int width, int height) =>
        Math.Min(1F, (float)MaxPreviewEdge / Math.Max(width, height));

    private static SKSizeI Fit(int width, int height)
    {
        var scale = ScaleFor(width, height);
        return new(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static void Orient(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(matrix);
    }

    private static SKMatrix Matrix(
        float scaleX,
        float skewX,
        float translateX,
        float skewY,
        float scaleY,
        float translateY) => new()
        {
            ScaleX = scaleX,
            SkewX = skewX,
            TransX = translateX,
            SkewY = skewY,
            ScaleY = scaleY,
            TransY = translateY,
            Persp2 = 1,
        };

    #endregion
}
