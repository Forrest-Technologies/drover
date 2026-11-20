namespace Drover.Engine.Files;

/// <summary>How a Flate stream's rows were predicted: the PDF predictor number and the shape of one row.</summary>
internal readonly record struct PdfPredictorShape(int Predictor, int Colors, int BitsPerComponent, int Columns)
{
    #region Properties

    public bool IsPng => Predictor is >= 10 and <= 15;

    public bool IsTiff => Predictor == 2;

    public long RowBytes => ((long)Colors * BitsPerComponent * Columns + 7) / 8;

    public int BytesPerPixel => Math.Max(1, ((Colors * BitsPerComponent) + 7) / 8);

    #endregion
}

/// <summary>
/// Reverses the row predictors of a Flate stream in place. The output is never longer than the input, so the decode
/// budget that bounded the input bounds this step too, and nothing is allocated here.
/// </summary>
internal static class PdfPredictor
{
    #region Public Methods

    /// <summary>Decodes <paramref name="data"/> in place and returns its new length, or the reason it was refused.</summary>
    public static NativePreviewRefusal? Decode(Span<byte> data, PdfPredictorShape shape, out int length)
    {
        length = data.Length;
        if (shape.Colors < 1 || shape.Columns < 1 || shape.BitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            return NativePreviewRefusal.Malformed;
        }

        var rowBytes = shape.RowBytes;
        if (rowBytes > data.Length && data.Length > 0)
        {
            return NativePreviewRefusal.Malformed;
        }

        if (shape.IsPng)
        {
            return DecodePng(data, shape, (int)rowBytes, out length);
        }

        if (shape.IsTiff)
        {
            return DecodeTiff(data, shape, (int)rowBytes);
        }

        return NativePreviewRefusal.TooComplex;
    }

    #endregion

    #region Private Methods

    private static NativePreviewRefusal? DecodePng(Span<byte> data, PdfPredictorShape shape, int rowBytes, out int length)
    {
        length = 0;
        var rowSize = rowBytes + 1;
        if (data.Length % rowSize != 0)
        {
            return NativePreviewRefusal.Malformed;
        }

        var rows = data.Length / rowSize;
        var bytesPerPixel = shape.BytesPerPixel;
        for (var row = 0; row < rows; row++)
        {
            var input = (row * rowSize) + 1;
            var output = row * rowBytes;
            if (data[input - 1] > 4)
            {
                return NativePreviewRefusal.Malformed;
            }

            UnfilterRow(data, data[input - 1], input, output, row > 0 ? output - rowBytes : -1, rowBytes, bytesPerPixel);
        }

        length = rows * rowBytes;
        return null;
    }

    private static void UnfilterRow(
        Span<byte> data, byte tag, int input, int output, int previous, int rowBytes, int bytesPerPixel)
    {
        for (var column = 0; column < rowBytes; column++)
        {
            var left = column >= bytesPerPixel ? data[output + column - bytesPerPixel] : 0;
            var up = previous >= 0 ? data[previous + column] : 0;
            var upLeft = previous >= 0 && column >= bytesPerPixel ? data[previous + column - bytesPerPixel] : 0;
            var predicted = tag switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0,
            };

            data[output + column] = (byte)(data[input + column] + predicted);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static NativePreviewRefusal? DecodeTiff(Span<byte> data, PdfPredictorShape shape, int rowBytes)
    {
        if (shape.BitsPerComponent != 8)
        {
            return NativePreviewRefusal.TooComplex;
        }

        for (var index = 0; index < data.Length; index++)
        {
            if (index % rowBytes >= shape.Colors)
            {
                data[index] += data[index - shape.Colors];
            }
        }

        return null;
    }

    #endregion
}
