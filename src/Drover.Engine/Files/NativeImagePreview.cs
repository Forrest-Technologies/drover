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

    internal static DecodeLimits DefaultLimits { get; } = new(
        MaxJpegScans: 64,
        MaxProgressivePixels: 16_000_000,
        Deadline: TimeSpan.FromSeconds(2));

    #endregion

    #region Public Methods

    public static NativeFilePreview Resize(byte[] bytes) =>
        Resize(bytes, DefaultLimits, TimeProvider.System);

    internal static NativeFilePreview Resize(byte[] bytes, DecodeLimits limits, TimeProvider time)
    {
        if (bytes.Length > MaxInputBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        if (!FileInspector.TryReadImageDimensions(bytes, out var width, out var height))
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        return ResizeFromHeader(bytes, width, height, limits, time);
    }

    internal static NativeFilePreview ResizeFromHeader(
        byte[] bytes, int width, int height, DecodeLimits limits, TimeProvider time)
    {
        if (!WithinSourceBounds(width, height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        if (!TryStripPngMetadata(bytes, out var stripped))
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        if (InspectJpegLayout(bytes, width, height, limits, out var progressive) is { } jpegRefusal)
        {
            return new(Refusal: jpegRefusal);
        }

        var started = time.GetTimestamp();
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
        if (source.Width != width || source.Height != height
            || !WithinSourceBounds(source.Width, source.Height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var decodeSize = DecodeSize(codec, source.Width, source.Height);
        if (!WithinDecodedBounds(decodeSize.Width, decodeSize.Height))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var decodeInfo = new SKImageInfo(
            decodeSize.Width,
            decodeSize.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        return Decode(
            codec,
            decodeInfo,
            source.Width,
            source.Height,
            progressive,
            limits.Deadline,
            time,
            started);
    }

    #endregion

    #region Private Methods

    private static NativeFilePreview Decode(
        SKCodec codec,
        SKImageInfo decodeInfo,
        int sourceWidth,
        int sourceHeight,
        bool progressive,
        TimeSpan deadline,
        TimeProvider time,
        long started)
    {
        using var decoded = new SKBitmap();
        if (!decoded.TryAllocPixels(decodeInfo))
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        if (Expired(time, started, deadline))
        {
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        if (codec.StartScanlineDecode(decodeInfo) is not SKCodecResult.Success)
        {
            return progressive
                ? new(Refusal: NativePreviewRefusal.TooComplex)
                : DecodeWithoutScanlines(codec, decoded, sourceWidth, sourceHeight, deadline, time, started);
        }

        for (var line = 0; line < decodeInfo.Height; line++)
        {
            if (Expired(time, started, deadline))
            {
                return new(Refusal: NativePreviewRefusal.TooComplex);
            }

            var outputLine = codec.GetOutputScanline(codec.NextScanline);
            if (outputLine < 0 || outputLine >= decodeInfo.Height)
            {
                return new(Refusal: NativePreviewRefusal.Malformed);
            }

            var destination = IntPtr.Add(decoded.GetPixels(), checked(outputLine * decoded.RowBytes));
            if (codec.GetScanlines(destination, 1, decoded.RowBytes) != 1)
            {
                return new(Refusal: NativePreviewRefusal.Malformed);
            }
        }

        return EncodeWithinDeadline(decoded, codec.EncodedOrigin, deadline, time, started);
    }

    private static NativeFilePreview DecodeWithoutScanlines(
        SKCodec codec,
        SKBitmap decoded,
        int sourceWidth,
        int sourceHeight,
        TimeSpan deadline,
        TimeProvider time,
        long started)
    {
        if ((long)sourceWidth * sourceHeight > MaxDecodedPixels)
        {
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not SKCodecResult.Success)
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        return Expired(time, started, deadline)
            ? new(Refusal: NativePreviewRefusal.TooComplex)
            : EncodeWithinDeadline(decoded, codec.EncodedOrigin, deadline, time, started);
    }

    private static NativeFilePreview EncodeWithinDeadline(
        SKBitmap decoded,
        SKEncodedOrigin origin,
        TimeSpan deadline,
        TimeProvider time,
        long started)
    {
        var preview = Encode(decoded, origin);
        return Expired(time, started, deadline)
            ? new(Refusal: NativePreviewRefusal.TooComplex)
            : preview;
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

        var rendered = encoded.ToArray();
        return TryStripPngMetadata(rendered, out var stripped, renderedCopy: true)
            ? new(Image: stripped is null ? rendered : stripped.WrittenSpan.ToArray())
            : new(Refusal: NativePreviewRefusal.Malformed);
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
        out ArrayBufferWriter<byte>? stripped,
        bool renderedCopy = false)
    {
        stripped = null;
        if (!IsPng(bytes))
        {
            return true;
        }

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
            var remove = renderedCopy
                ? !type.SequenceEqual("IHDR"u8) && !type.SequenceEqual("IDAT"u8) && !type.SequenceEqual("IEND"u8)
                : type.SequenceEqual("iCCP"u8) || type.SequenceEqual("zTXt"u8) || type.SequenceEqual("iTXt"u8);
            if (remove)
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

    private static SKSizeI DecodeSize(SKCodec codec, int width, int height)
    {
        var scale = ScaleFor(width, height);
        var size = codec.GetScaledDimensions(scale);
        while (Math.Max(width, height) > MaxPreviewEdge
            && Math.Max(size.Width, size.Height) < MaxPreviewEdge
            && scale < 1F)
        {
            scale = Math.Min(1F, scale * 2F);
            size = codec.GetScaledDimensions(scale);
        }

        return size;
    }

    private static NativePreviewRefusal? InspectJpegLayout(
        ReadOnlySpan<byte> bytes,
        int width,
        int height,
        DecodeLimits limits,
        out bool progressive)
    {
        progressive = false;
        if (!IsJpeg(bytes))
        {
            return null;
        }

        var position = 2;
        var scans = 0;
        var inScan = false;
        while (position < bytes.Length)
        {
            if (bytes[position] != 0xFF)
            {
                if (!inScan)
                {
                    return NativePreviewRefusal.Malformed;
                }

                position++;
                continue;
            }

            while (position < bytes.Length && bytes[position] == 0xFF)
            {
                position++;
            }

            if (position >= bytes.Length)
            {
                return NativePreviewRefusal.Malformed;
            }

            var marker = bytes[position++];
            if (marker == 0x00 || marker is >= 0xD0 and <= 0xD7)
            {
                if (!inScan)
                {
                    return NativePreviewRefusal.Malformed;
                }

                continue;
            }

            inScan = false;
            if (marker == 0xD9)
            {
                return null;
            }

            if (marker is 0x01 or 0xD8)
            {
                continue;
            }

            if (position > bytes.Length - 2)
            {
                return NativePreviewRefusal.Malformed;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
            if (length < 2 || length > bytes.Length - position)
            {
                return NativePreviewRefusal.Malformed;
            }

            if (marker is 0xC2 or 0xC6 or 0xCA or 0xCE)
            {
                progressive = true;
                if ((long)width * height > limits.MaxProgressivePixels)
                {
                    return NativePreviewRefusal.TooComplex;
                }
            }

            if (marker == 0xDA && ++scans > limits.MaxJpegScans)
            {
                return NativePreviewRefusal.TooComplex;
            }

            position += length;
            inScan = marker == 0xDA;
        }

        return NativePreviewRefusal.Malformed;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes is [0xFF, 0xD8, 0xFF, ..];

    private static bool Expired(TimeProvider time, long started, TimeSpan deadline) =>
        time.GetElapsedTime(started) >= deadline;

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

    #region Nested Types

    internal sealed record DecodeLimits(int MaxJpegScans, long MaxProgressivePixels, TimeSpan Deadline);

    #endregion
}
