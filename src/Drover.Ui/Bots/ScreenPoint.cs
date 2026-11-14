namespace Drover.Ui.Bots;

/// <summary>
/// Where on a sandbox's screen a person means. The picture is shown scaled to the width it has, on a phone as on a
/// desk, so a tap on the picture is mapped from the size it is shown at to the size the screen really is. The
/// keyboard moves a pointer over the picture in screen pixels.
/// </summary>
public static class ScreenPoint
{
    #region Properties

    /// <summary>How far an arrow key moves the pointer; with Shift, <see cref="FarStep"/>.</summary>
    public const int Step = 16;

    public const int FarStep = 64;

    #endregion

    #region Public Methods

    /// <summary>
    /// The screen pixel under a point of the shown picture: <paramref name="offsetX"/> and <paramref name="offsetY"/>
    /// from its top left corner, on a picture shown <paramref name="shownWidth"/> by <paramref name="shownHeight"/>
    /// of a screen <paramref name="screenWidth"/> by <paramref name="screenHeight"/>. Always on the screen.
    /// </summary>
    public static (int X, int Y) FromShown(
        double offsetX,
        double offsetY,
        double shownWidth,
        double shownHeight,
        int screenWidth,
        int screenHeight) =>
        (Scale(offsetX, shownWidth, screenWidth), Scale(offsetY, shownHeight, screenHeight));

    /// <summary>The pointer moved by an arrow key, kept on the screen. Null for any other key.</summary>
    public static (int X, int Y)? Move((int X, int Y) from, string key, bool far, int screenWidth, int screenHeight)
    {
        var step = far ? FarStep : Step;
        (int X, int Y)? moved = key switch
        {
            "ArrowLeft" => (from.X - step, from.Y),
            "ArrowRight" => (from.X + step, from.Y),
            "ArrowUp" => (from.X, from.Y - step),
            "ArrowDown" => (from.X, from.Y + step),
            _ => null,
        };
        return moved is { } point ? (Math.Clamp(point.X, 0, screenWidth - 1), Math.Clamp(point.Y, 0, screenHeight - 1)) : null;
    }

    /// <summary>Where the pointer is drawn over the shown picture, as a share of its width and height in percent.</summary>
    public static (double Left, double Top) Percent((int X, int Y) point, int screenWidth, int screenHeight) =>
        (100.0 * (point.X + 0.5) / screenWidth, 100.0 * (point.Y + 0.5) / screenHeight);

    #endregion

    #region Private Methods

    private static int Scale(double at, double shown, int size) =>
        shown <= 0 || size <= 0 ? 0 : Math.Clamp((int)Math.Floor(at * size / shown), 0, size - 1);

    #endregion
}
