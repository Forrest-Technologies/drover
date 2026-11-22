namespace Drover.Engine.Files;

/// <summary>Decodes PDF predictors only after their geometry and bounded output size are known.</summary>
internal static class PdfPreviewPredictor
{
    #region Private Fields

    private const int DefaultBitsPerComponent = 8;
    private const int DefaultColors = 1;
    private const int DefaultColumns = 1;
    private const int MaxColors = 32;

    #endregion

    #region Public Methods

    public static bool TryDecode(
        Memory<byte> input,
        PdfTokens.DictionaryToken? parameters,
        int maxStreamBytes,
        CancellationToken cancellationToken,
        out Memory<byte> result)
    {
        result = default;
        if (!TryReadParameters(parameters, out var predictor, out var colors, out var bitsPerComponent, out var columns))
        {
            return false;
        }

        if (predictor == 1)
        {
            result = input;
            return true;
        }

        if (!TryGetRowGeometry(colors, bitsPerComponent, columns, maxStreamBytes, out var rowBytes))
        {
            return false;
        }

        return predictor switch
        {
            2 => TryDecodeTiff(input, colors, bitsPerComponent, columns, rowBytes, cancellationToken, out result),
            >= 10 and <= 15 => TryDecodePng(input, colors, bitsPerComponent, rowBytes, cancellationToken, out result),
            _ => false,
        };
    }

    #endregion

    #region Private Methods

    private static void DecodePngRow(Span<byte> row, ReadOnlySpan<byte> prior, int bytesPerPixel, int filter)
    {
        for (var index = 0; index < row.Length; index++)
        {
            var left = index >= bytesPerPixel ? row[index - bytesPerPixel] : 0;
            var above = prior.IsEmpty ? 0 : prior[index];
            var upperLeft = !prior.IsEmpty && index >= bytesPerPixel ? prior[index - bytesPerPixel] : 0;
            var predicted = filter switch
            {
                0 => 0,
                1 => left,
                2 => above,
                3 => (left + above) / 2,
                4 => Paeth(left, above, upperLeft),
                _ => 0,
            };
            row[index] = unchecked((byte)(row[index] + predicted));
        }
    }

    private static void DecodeTiffPackedRow(Span<byte> row, int colors, int bitsPerComponent, int columns)
    {
        var mask = (1 << bitsPerComponent) - 1;
        var samples = colors * columns;
        for (var sample = colors; sample < samples; sample++)
        {
            var value = ReadPackedSample(row, sample, bitsPerComponent, mask);
            var left = ReadPackedSample(row, sample - colors, bitsPerComponent, mask);
            WritePackedSample(row, sample, bitsPerComponent, mask, value + left);
        }
    }

    private static void DecodeTiffRow(Span<byte> row, int colors, int bitsPerComponent, int columns)
    {
        if (bitsPerComponent is 1 or 2 or 4)
        {
            DecodeTiffPackedRow(row, colors, bitsPerComponent, columns);
            return;
        }

        var bytesPerPixel = colors * bitsPerComponent / 8;
        if (bitsPerComponent == 8)
        {
            for (var index = bytesPerPixel; index < row.Length; index++)
            {
                row[index] = unchecked((byte)(row[index] + row[index - bytesPerPixel]));
            }

            return;
        }

        for (var index = bytesPerPixel; index < row.Length - 1; index += 2)
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(row[index..]);
            var left = BinaryPrimitives.ReadUInt16BigEndian(row[(index - bytesPerPixel)..]);
            BinaryPrimitives.WriteUInt16BigEndian(row[index..], unchecked((ushort)(value + left)));
        }
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);

        if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance)
        {
            return left;
        }

        return aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static int ReadPackedSample(ReadOnlySpan<byte> row, int sample, int bitsPerComponent, int mask)
    {
        var bitOffset = sample * bitsPerComponent;
        var shift = 8 - (bitOffset % 8) - bitsPerComponent;
        return row[bitOffset / 8] >> shift & mask;
    }

    private static bool TryDecodePng(
        ReadOnlyMemory<byte> input,
        int colors,
        int bitsPerComponent,
        int rowBytes,
        CancellationToken cancellationToken,
        out Memory<byte> result)
    {
        result = default;
        var encodedRowBytes = rowBytes + 1;
        if (input.Length % encodedRowBytes != 0)
        {
            return false;
        }

        var rows = input.Length / encodedRowBytes;
        var decoded = new byte[rows * rowBytes];
        var bytesPerPixel = (colors * bitsPerComponent + 7) / 8;
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encodedOffset = rowIndex * encodedRowBytes;
            var filter = input.Span[encodedOffset];
            if (filter > 4)
            {
                return false;
            }

            var row = decoded.AsSpan(rowIndex * rowBytes, rowBytes);
            input.Span.Slice(encodedOffset + 1, rowBytes).CopyTo(row);
            var prior = rowIndex == 0
                ? ReadOnlySpan<byte>.Empty
                : decoded.AsSpan((rowIndex - 1) * rowBytes, rowBytes);
            DecodePngRow(row, prior, bytesPerPixel, filter);
        }

        result = decoded;
        return true;
    }

    private static bool TryDecodeTiff(
        ReadOnlyMemory<byte> input,
        int colors,
        int bitsPerComponent,
        int columns,
        int rowBytes,
        CancellationToken cancellationToken,
        out Memory<byte> result)
    {
        result = default;
        if (input.Length % rowBytes != 0)
        {
            return false;
        }

        var decoded = input.ToArray();
        for (var offset = 0; offset < decoded.Length; offset += rowBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodeTiffRow(decoded.AsSpan(offset, rowBytes), colors, bitsPerComponent, columns);
        }

        result = decoded;
        return true;
    }

    private static bool TryGetInteger(
        PdfTokens.DictionaryToken? parameters,
        PdfTokens.NameToken name,
        int defaultValue,
        out int value)
    {
        value = defaultValue;
        if (parameters is null || !parameters.TryGet(name, out var token))
        {
            return true;
        }

        if (token is not PdfTokens.NumericToken numeric
            || !double.IsFinite(numeric.Double)
            || numeric.Double != Math.Truncate(numeric.Double)
            || numeric.Double is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)numeric.Double;
        return true;
    }

    private static bool TryGetRowGeometry(
        int colors,
        int bitsPerComponent,
        int columns,
        int maxStreamBytes,
        out int rowBytes)
    {
        var rowBits = (long)colors * bitsPerComponent * columns;
        var calculated = (rowBits + 7) / 8;
        if (calculated is <= 0 || calculated > maxStreamBytes)
        {
            rowBytes = 0;
            return false;
        }

        rowBytes = (int)calculated;
        return true;
    }

    private static bool TryReadParameters(
        PdfTokens.DictionaryToken? parameters,
        out int predictor,
        out int colors,
        out int bitsPerComponent,
        out int columns)
    {
        predictor = 1;
        colors = DefaultColors;
        bitsPerComponent = DefaultBitsPerComponent;
        columns = DefaultColumns;
        return TryGetInteger(parameters, PdfTokens.NameToken.Predictor, 1, out predictor)
            && TryGetInteger(parameters, PdfTokens.NameToken.Colors, DefaultColors, out colors)
            && colors is > 0 and <= MaxColors
            && TryGetInteger(
                parameters,
                PdfTokens.NameToken.BitsPerComponent,
                DefaultBitsPerComponent,
                out bitsPerComponent)
            && bitsPerComponent is 1 or 2 or 4 or 8 or 16
            && TryGetInteger(parameters, PdfTokens.NameToken.Columns, DefaultColumns, out columns)
            && columns > 0;
    }

    private static void WritePackedSample(
        Span<byte> row,
        int sample,
        int bitsPerComponent,
        int mask,
        int value)
    {
        var bitOffset = sample * bitsPerComponent;
        var byteIndex = bitOffset / 8;
        var shift = 8 - (bitOffset % 8) - bitsPerComponent;
        var shiftedMask = mask << shift;
        row[byteIndex] = (byte)((row[byteIndex] & ~shiftedMask) | (value & mask) << shift);
    }

    #endregion
}
