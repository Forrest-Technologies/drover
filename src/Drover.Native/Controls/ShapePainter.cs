namespace Drover.Native.Controls;

/// <summary>Draws the shapes of a drawing kept as data: an icon's strokes, the parts of a bot's character.</summary>
internal static class ShapePainter
{
    #region Public Methods

    public static SKColor ToSkia(this Color color) => new(color.R, color.G, color.B, color.A);

    public static SKColor ToSkia(this Rgb color) => new(color.Red, color.Green, color.Blue);

    public static Rgb ToRgb(this Color color) => new(color.R, color.G, color.B);

    public static Color ToColor(this Rgb color) => Color.FromArgb(byte.MaxValue, color.Red, color.Green, color.Blue);

    /// <summary>The outline of one shape, as a path that can be filled, stroked, or clipped to.</summary>
    public static SKPath Outline(SvgShape shape) => SKPath.ParseSvgPathData(shape.PathData);

    /// <summary>All the shapes as one outline, for clipping to.</summary>
    public static SKPath Outline(IEnumerable<SvgShape> shapes) =>
        SKPath.ParseSvgPathData(string.Join(' ', shapes.Select(shape => shape.PathData)));

    public static void Fill(SKCanvas canvas, SvgShape shape, SKColor color)
    {
        using var outline = Outline(shape);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };
        canvas.DrawPath(outline, paint);
    }

    public static void Stroke(SKCanvas canvas, SvgShape shape, SKColor color, float width, bool round)
    {
        using var outline = Outline(shape);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = width,
            StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Square,
            StrokeJoin = round ? SKStrokeJoin.Round : SKStrokeJoin.Miter,
            IsAntialias = true,
        };
        canvas.DrawPath(outline, paint);
    }

    #endregion
}
