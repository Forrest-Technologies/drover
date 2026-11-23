namespace Drover.Native.Chat;

/// <summary>
/// A picture taken from the clipboard, made safe to attach. The bytes come from another program, so how much is read
/// and how large a picture may be are bounded before anything decodes it, and what leaves is always a PNG.
/// </summary>
internal static class PastedPicture
{
    #region Private Fields

    // Past this a picture is not read at all. The server's own limit is lower, and says so on the file's chip.
    private const long MaxBytes = 64L * 1024 * 1024;

    // A picture of 8K by 4K. The decoded pixels of a larger one would hold too much memory on a phone.
    private const long MaxPixels = 32_000_000;

    #endregion

    #region Public Methods

    /// <summary>The most bytes of a picture that are read from the clipboard.</summary>
    public static long Limit => MaxBytes;

    /// <summary>
    /// The picture as a PNG, or null when the bytes are not one still picture within the limits. A PNG that fits is
    /// kept as it is, since it is checked and not drawn; any other format is decoded and written again as PNG.
    /// </summary>
    public static byte[]? ToPng(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        if (codec is null || codec.FrameCount > 1 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        if (codec.EncodedFormat is SKEncodedImageFormat.Png)
        {
            return bytes;
        }

        using var bitmap = SKBitmap.Decode(codec);
        using var image = bitmap is null ? null : SKImage.FromBitmap(bitmap);
        using var png = image?.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }

    /// <summary>
    /// Reads the clipboard's stream up to the limit. Null when the stream holds more than that: the size is then said
    /// by <paramref name="size"/>, so the chip can tell the person it is too large.
    /// </summary>
    public static async Task<byte[]?> Read(Stream source, long size, CancellationToken cancellationToken)
    {
        if (size > MaxBytes)
        {
            return null;
        }

        using var held = new MemoryStream();
        var buffer = new byte[81920];
        int count;

        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await held.WriteAsync(buffer.AsMemory(0, count), cancellationToken);

            if (held.Length > MaxBytes)
            {
                return null;
            }
        }

        return held.ToArray();
    }

    #endregion
}
