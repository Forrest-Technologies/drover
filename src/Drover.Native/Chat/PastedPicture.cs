namespace Drover.Native.Chat;

/// <summary>
/// A picture taken from the clipboard, made safe to attach. The bytes come from another program, so how much is read
/// and how large a picture may be are bounded before anything decodes it, and what leaves is always a PNG.
/// </summary>
internal static class PastedPicture
{
    #region Private Fields

    // Past this a picture is not read at all. The server's own limit is lower, and says so on the file's chip.
    private const int MaxBytes = 64 * 1024 * 1024;

    // A picture of 8K by 4K. The decoded pixels of a larger one would hold too much memory on a phone.
    private const long MaxPixels = 32_000_000;

    // How much the read buffer starts with when the stream does not say its size.
    private const int FirstRead = 81920;

    #endregion

    #region Properties

    /// <summary>The most bytes of a picture that are read from the clipboard.</summary>
    public static int Limit => MaxBytes;

    #endregion

    #region Public Methods

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
    /// Reads the clipboard's stream, never more than <paramref name="most"/> bytes: each read asks for no more than
    /// what is left of that, straight into a buffer that never grows past it. Null when the stream holds more, whatever
    /// <paramref name="size"/> it said it had; one byte past the limit is asked for to tell that.
    /// </summary>
    public static async Task<byte[]?> Read(Stream source, long size, int most, CancellationToken cancellationToken)
    {
        if (size > most)
        {
            return null;
        }

        var held = new byte[(int)Math.Max(size, 0)];
        var length = 0;

        while (length < most)
        {
            if (length == held.Length)
            {
                Array.Resize(ref held, (int)Math.Min(most, Math.Max(FirstRead, 2L * held.Length)));
            }

            var count = await source.ReadAsync(held.AsMemory(length), cancellationToken);

            if (count == 0)
            {
                return held.AsSpan(0, length).ToArray();
            }

            length += count;
        }

        var past = await source.ReadAsync(new byte[1], cancellationToken);
        return past == 0 ? held : null;
    }

    #endregion
}
