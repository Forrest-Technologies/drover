namespace Drover.Native.Controls;

/// <summary>
/// The outline of a frame in a corner language, as one closed run of points, clockwise from the top edge. The frame
/// draws its edge on it and the line of live work travels it, so the two always lie on the same shape.
/// </summary>
internal static class FrameOutline
{
    #region Private Fields

    // A quarter turn of a rounded corner is drawn in this many straight pieces, of the half turn's 180 degrees.
    private const int RoundedSteps = 8;

    private const double QuarterTurn = 90;

    private const double HalfTurn = 180;

    #endregion

    #region Public Methods

    /// <summary>
    /// The outline of a frame of this size, moved in by <paramref name="inset"/> so a line of twice that width lies
    /// wholly inside it. A faceted frame cuts its corners by <paramref name="cut"/>; a square or rounded one rounds
    /// them by <paramref name="radius"/>, up to half its shorter side; any other is a plain rectangle.
    /// </summary>
    public static IReadOnlyList<Point> Of(CornerLanguage corners, double width, double height, double inset, double cut, double radius) =>
        corners switch
        {
            CornerLanguage.Faceted => CutCorners(width, height, inset, cut),
            CornerLanguage.Square or CornerLanguage.Rounded => Rounded(width, height, inset, radius),
            _ => Rounded(width, height, inset, 0),
        };

    /// <summary>The theme's cut for a frame in this role.</summary>
    public static double CutOf(FacetRole role) => Tokens.Number(role is FacetRole.Control ? "DvCutS" : "DvCut");

    /// <summary>The theme's corner radius for a frame in this role.</summary>
    public static double RadiusOf(FacetRole role) =>
        Tokens.Number(role is FacetRole.Control ? "DvRadiusControl" : "DvRadiusCard");

    #endregion

    #region Private Methods

    private static Point[] CutCorners(double width, double height, double inset, double cut) =>
    [
        new(cut, inset),
        new(width - inset, inset),
        new(width - inset, height - cut),
        new(width - cut, height - inset),
        new(inset, height - inset),
        new(inset, cut),
    ];

    // A rounded rectangle as short straight pieces, so it is one polygon like the others.
    private static List<Point> Rounded(double width, double height, double inset, double radius)
    {
        var round = Math.Max(0, Math.Min(radius - inset, Math.Min(width, height) / 2));
        List<Point> points = [];

        foreach (var (x, y, from) in new[]
        {
            (width - inset - round, inset + round, -90.0),
            (width - inset - round, height - inset - round, 0.0),
            (inset + round, height - inset - round, 90.0),
            (inset + round, inset + round, 180.0),
        })
        {
            for (var step = 0; step <= RoundedSteps; step++)
            {
                var angle = (from + (QuarterTurn * step / RoundedSteps)) * Math.PI / HalfTurn;
                points.Add(new(x + (round * Math.Cos(angle)), y + (round * Math.Sin(angle))));
            }
        }

        return points;
    }

    #endregion
}
