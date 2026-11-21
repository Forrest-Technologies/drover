namespace Drover.Ui.Chat;

/// <summary>
/// Where a conversation list scrolls to land on one message. The offset puts that row in the middle of the
/// viewport, and never past the ends of the list. Staying at the foot keeps the newest row in view.
/// </summary>
public static class MomentViewport
{
    /// <summary>The scroll offset that brings the row into the middle of the viewport.</summary>
    public static double Offset(double rowTop, double rowHeight, double viewportHeight, double scrollableHeight)
    {
        var middle = rowTop + (rowHeight / 2) - (viewportHeight / 2);
        return Math.Clamp(middle, 0, Math.Max(0, scrollableHeight));
    }

    /// <summary>True when the whole row sits inside the viewport at that offset.</summary>
    public static bool Shows(double offset, double rowTop, double rowHeight, double viewportHeight)
    {
        if (rowHeight <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        var top = rowTop - offset;
        var bottom = top + rowHeight;
        return top >= 0 && bottom <= viewportHeight;
    }
}
