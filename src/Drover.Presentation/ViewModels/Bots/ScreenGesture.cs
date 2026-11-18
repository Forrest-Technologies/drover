namespace Drover.Presentation.ViewModels.Bots;

/// <summary>
/// The fingers or the mouse on a sandbox's picture, from the first one down to the last one up: a pinch enlarges the
/// picture and a drag moves it, shown at once and told to the screen when the gesture ends; a press that neither moved
/// nor pinched is a tap. A drag or a pinch never clicks in the sandbox. Places are measured from the top left corner
/// of the picture's frame.
/// </summary>
public sealed class ScreenGesture
{
    #region Private Fields

    private readonly Dictionary<uint, (double X, double Y)> pointers = [];

    private bool moved;

    private bool pinched;

    private ScreenZoom view = ScreenZoom.Fit;

    private ScreenZoom from = ScreenZoom.Fit;

    private (double X, double Y, double Spread) start;

    #endregion

    #region Properties

    /// <summary>How far a pointer may travel and still be a tap.</summary>
    public const double TapSlop = 10;

    /// <summary>True while at least one pointer is down on the picture.</summary>
    public bool IsActive => pointers.Count > 0;

    #endregion

    #region Public Methods

    /// <summary>A pointer went down on the picture, which shows <paramref name="shown"/> when it is the first one.</summary>
    public void Down(uint id, double x, double y, ScreenZoom shown)
    {
        if (pointers.Count == 0)
        {
            (moved, pinched, view) = (false, false, shown);
        }

        pointers[id] = (x, y);
        pinched |= pointers.Count > 1;
        Rebase();
    }

    /// <summary>
    /// A pointer moved over a frame <paramref name="width"/> by <paramref name="height"/>. The view to show at once,
    /// or null when nothing changes: the pointer is not one of this gesture's, it has not left the place it went down,
    /// or it drags a picture that is not enlarged.
    /// </summary>
    public ScreenZoom? Move(uint id, double x, double y, double width, double height)
    {
        if (!pointers.ContainsKey(id) || width <= 0 || height <= 0)
        {
            return null;
        }

        pointers[id] = (x, y);
        var now = Centre();
        var travel = Math.Sqrt(Math.Pow(now.X - start.X, 2) + Math.Pow(now.Y - start.Y, 2));
        var pinching = pointers.Count > 1;

        if (!moved && !pinching && travel < TapSlop)
        {
            return null;
        }

        moved = true;
        if (!pinching && from.Scale <= 1)
        {
            return null;
        }

        var scale = Math.Clamp(from.Scale * (pinching ? now.Spread / start.Spread : 1), 1, ScreenZoom.MaxScale);
        var (fromX, fromY) = (start.X / width, start.Y / height);
        var (toX, toY) = (now.X / width, now.Y / height);
        view = new(
            scale,
            Math.Clamp(toX - ((fromX - from.X) / from.Scale * scale), 1 - scale, 0),
            Math.Clamp(toY - ((fromY - from.Y) / from.Scale * scale), 1 - scale, 0));
        return view;
    }

    /// <summary>
    /// A pointer went up, or was taken away by the system. When it was the last one, says what the gesture was: a tap,
    /// or a view to keep.
    /// </summary>
    public ScreenGestureEnd Up(uint id, bool cancelled = false)
    {
        if (!pointers.Remove(id))
        {
            return default;
        }

        if (pointers.Count > 0)
        {
            Rebase();
            return default;
        }

        return moved ? new(false, view) : new(!cancelled && !pinched, null);
    }

    #endregion

    #region Private Methods

    private void Rebase()
    {
        from = view;
        start = Centre();
    }

    private (double X, double Y, double Spread) Centre()
    {
        var points = pointers.Values.ToList();
        var spread = points.Count > 1
            ? Math.Sqrt(Math.Pow(points[0].X - points[1].X, 2) + Math.Pow(points[0].Y - points[1].Y, 2))
            : 1;
        return (points.Average(point => point.X), points.Average(point => point.Y), spread > 0 ? spread : 1);
    }

    #endregion
}

/// <summary>How a gesture on the picture ended: with a tap, with a view to keep, or with neither.</summary>
public readonly record struct ScreenGestureEnd(bool Tapped, ScreenZoom? View);
