namespace Drover.Engine.Tests.Sandboxes;

/// <summary>The start of a PNG file of a given size: its signature and header chunk, which is all the size is read from.</summary>
public static class TestPng
{
    #region Public Methods

    public static byte[] Make(int width, int height)
    {
        var png = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        png[24] = 8;
        png[25] = 2;
        return png;
    }

    #endregion
}
