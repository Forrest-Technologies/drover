namespace Drover.Ui.Bots;

/// <summary>
/// How much of a sandbox's screen the picture shows: enlarged <see cref="Scale"/> times, and moved by
/// <see cref="X"/> and <see cref="Y"/>, each a share of the shown width and height (0 at fit, down to
/// <c>1 - Scale</c> when the far edge shows). Shares, not pixels, so the same view holds while the picture is
/// shown smaller, as it is while the on-screen keyboard is up. The view always covers the whole picture frame.
/// </summary>
public readonly record struct ScreenZoom(double Scale, double X, double Y)
{
    #region Properties

    public const double MaxScale = 4;

    /// <summary>How far an arrow key moves the view, as a share of what shows; with Shift, <see cref="FarPan"/>.</summary>
    public const double Pan = 0.1;

    public const double FarPan = 0.4;

    /// <summary>The steps the zoom buttons go through. A pinch can stop anywhere between them.</summary>
    public static IReadOnlyList<double> Steps { get; } = [1, 1.5, 2, 3, MaxScale];

    /// <summary>The whole screen, as large as the frame.</summary>
    public static ScreenZoom Fit { get; } = new(1, 0, 0);

    public bool IsFit => Scale <= 1;

    public bool IsMax => Scale >= MaxScale;

    /// <summary>The zoom in percent, as a person reads it.</summary>
    public string Percent => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Scale * 100):0}%");

    #endregion

    #region Public Methods

    /// <summary>The next step in, keeping the middle of what shows where it is.</summary>
    public ScreenZoom ZoomedIn()
    {
        var scale = Scale;
        return ZoomedTo(Steps.FirstOrDefault(step => step > scale + 0.01, MaxScale));
    }

    /// <summary>The next step out, keeping the middle of what shows where it is.</summary>
    public ScreenZoom ZoomedOut()
    {
        var scale = Scale;
        return ZoomedTo(Steps.LastOrDefault(step => step < scale - 0.01, 1));
    }

    /// <summary>
    /// Enlarged to <paramref name="scale"/>, keeping the point at <paramref name="aroundX"/> and
    /// <paramref name="aroundY"/> of the frame (shares of its width and height) under the same place.
    /// </summary>
    public ScreenZoom ZoomedTo(double scale, double aroundX = 0.5, double aroundY = 0.5)
    {
        var next = Math.Clamp(scale, 1, MaxScale);
        return new ScreenZoom(
            next,
            aroundX - ((aroundX - X) / Scale * next),
            aroundY - ((aroundY - Y) / Scale * next)).Clamped();
    }

    /// <summary>The view moved by an arrow key, as a person looks further that way. Null for any other key.</summary>
    public ScreenZoom? Moved(string key, bool far)
    {
        var step = far ? FarPan : Pan;
        return key switch
        {
            "ArrowLeft" => (this with { X = X + step }).Clamped(),
            "ArrowRight" => (this with { X = X - step }).Clamped(),
            "ArrowUp" => (this with { Y = Y + step }).Clamped(),
            "ArrowDown" => (this with { Y = Y - step }).Clamped(),
            _ => null,
        };
    }

    /// <summary>
    /// The view moved as little as it takes for a point of the screen, given as shares of its width and height, to
    /// show with a margin, so a pointer moved by the keyboard never leaves the view.
    /// </summary>
    public ScreenZoom Showing(double shareX, double shareY) =>
        new ScreenZoom(Scale, Follow(X, shareX), Follow(Y, shareY)).Clamped();

    /// <summary>The same view kept inside its bounds: a scale from 1 to <see cref="MaxScale"/>, no edge showing past the frame.</summary>
    public ScreenZoom Clamped()
    {
        var scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 1, MaxScale) : 1;
        return new(scale, Bounded(X, scale), Bounded(Y, scale));
    }

    #endregion

    #region Private Methods

    private double Follow(double offset, double share)
    {
        var margin = 0.1 / Scale;
        var first = -offset / Scale;
        var shown = 1 / Scale;

        if (share < first + margin)
        {
            first = share - margin;
        }
        else if (share > first + shown - margin)
        {
            first = share - shown + margin;
        }

        return -first * Scale;
    }

    private static double Bounded(double offset, double scale) =>
        double.IsFinite(offset) ? Math.Clamp(offset, 1 - scale, 0) : 0;

    #endregion
}
