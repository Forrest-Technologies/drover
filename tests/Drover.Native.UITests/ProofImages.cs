namespace Drover.Native.UITests;

/// <summary>
/// The two pictures of the Android proof, drawn here so no file is checked in: the picture a person attaches, and
/// the sandbox's screen with a window on it that a drag can take hold of. Both are real PNGs that an app decodes.
/// </summary>
internal static class ProofImages
{
    #region Public Methods

    /// <summary>A 640 by 400 picture with stripes, as a phone's screenshot has many colors.</summary>
    public static byte[] Attachment() => Png(640, 400, (x, y) =>
        (
            (byte)((x * 255 / 640) ^ (y & 0x3F)),
            (byte)(y * 255 / 400),
            (byte)(((x / 40) + (y / 40)) % 2 == 0 ? 220 : 60)));

    /// <summary>The sandbox's screen, 1024 by 768: a dark desktop and one window with a title bar.</summary>
    public static byte[] Screen() => Png(1024, 768, (x, y) =>
        y is >= 150 and < 180 && x is >= 200 and < 700 ? ((byte)40, (byte)90, (byte)200)
        : y is >= 180 and < 500 && x is >= 200 and < 700 ? ((byte)235, (byte)235, (byte)235)
        : ((byte)(20 + (y / 8)), (byte)30, (byte)(40 + (x / 16))));

    #endregion

    #region Private Methods

    private static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        var at = 0;

        for (var y = 0; y < height; y++)
        {
            raw[at++] = 0;

            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = pixel(x, y);
                raw[at++] = red;
                raw[at++] = green;
                raw[at++] = blue;
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(type);
        png.Write(data);
        var checksum = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, checksum);
        png.Write(number);
    }

    private static uint Crc(uint start, ReadOnlySpan<byte> bytes)
    {
        var crc = start;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) is 1 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }

    #endregion
}
